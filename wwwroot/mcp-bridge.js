#!/usr/bin/env node
// Локальный MCP-мост для процедурного генератора баннеров.
// Работает без внешних npm-зависимостей (только встроенные модули Node.js).
// Режимы запуска:
//   node mcp-bridge.js            – стандартный MCP-сервер (stdio) + WebSocket-мост на 127.0.0.1:5219
//   node mcp-bridge.js --daemon   – запуск фонового моста на 127.0.0.1:5219 (для мгновенного коннекта вкладки)
//   node mcp-bridge.js --serve    – работа моста в текущем процессе (HTTP /mcp, /rpc, /health + WS /ws)
//   node mcp-bridge.js status     – проверка связи с мостом и вкладкой браузера
//   node mcp-bridge.js call <tool> [jsonArgs|@args.json] [--args-file args.json] [--stdin] [--out banner.png] – прямой вызов инструмента из CLI

'use strict';

const http = require('node:http');
const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');

const PORT = Number(process.env.BANNER_MCP_PORT || 5219);
const HOST = '127.0.0.1';
const WS_GUID = '258EAFA5-E914-47DA-95CA-C5AB0DC85B11';

const MAX_HTTP_BODY_BYTES = 256 * 1024;
const MAX_WS_HELLO_BYTES = 16 * 1024;
const MAX_WS_MESSAGE_BYTES = 16 * 1024 * 1024;
const MAX_WS_FRAGMENTS = 64;
const MAX_WS_CLIENTS = 8;
const MAX_STDIO_MESSAGE_BYTES = 1024 * 1024;

const DEFAULT_ALLOWED_ORIGINS = new Set([
    'https://thevsakeeper.github.io',
    'http://localhost:5209',
    'https://localhost:7276',
    'http://127.0.0.1:5209',
    'https://127.0.0.1:7276',
]);

const AUTH_FILE =
    process.env.BANNER_MCP_TOKEN_FILE ||
    (path.basename(__dirname) === '.banner-mcp'
        ? path.join(__dirname, '.mcp-auth.json')
        : path.join(os.tmpdir(), `banner-mcp-${PORT}.auth.json`));

let cachedAuth = { token: '', origins: [], mtimeMs: -1 };

function normalizeOrigin(raw) {
    if (typeof raw !== 'string' || !raw.trim()) return null;
    try {
        const u = new URL(raw.trim());
        if (u.protocol !== 'http:' && u.protocol !== 'https:') return null;
        return u.origin;
    } catch {
        return null;
    }
}

function parseCliAuthOverrides(argv) {
    let token = process.env.BANNER_MCP_TOKEN ? String(process.env.BANNER_MCP_TOKEN).trim() : '';
    const origins = [];
    if (process.env.BANNER_MCP_ORIGIN) {
        for (const part of String(process.env.BANNER_MCP_ORIGIN).split(',')) {
            const norm = normalizeOrigin(part);
            if (norm && !origins.includes(norm)) origins.push(norm);
        }
    }
    for (let i = 0; i < argv.length; i++) {
        if (argv[i] === '--token' && i + 1 < argv.length) {
            token = String(argv[++i]).trim();
        } else if (argv[i] === '--origin' && i + 1 < argv.length) {
            const norm = normalizeOrigin(argv[++i]);
            if (norm && !origins.includes(norm)) origins.push(norm);
        }
    }
    return { token, origins };
}

function initAuthFromCli(argv) {
    const cli = parseCliAuthOverrides(argv);
    let fileData = { token: '', origins: [] };
    try {
        if (fs.existsSync(AUTH_FILE)) {
            const parsed = JSON.parse(fs.readFileSync(AUTH_FILE, 'utf8'));
            if (parsed && typeof parsed === 'object') {
                if (typeof parsed.token === 'string') fileData.token = parsed.token.trim();
                if (Array.isArray(parsed.origins)) {
                    for (const o of parsed.origins) {
                        const norm = normalizeOrigin(o);
                        if (norm && !fileData.origins.includes(norm)) fileData.origins.push(norm);
                    }
                }
            }
        }
    } catch {
        // игнорируем повреждённый файл сопряжения
    }

    const token =
        cli.token && cli.token.length >= 16
            ? cli.token
            : fileData.token && fileData.token.length >= 16
                ? fileData.token
                : crypto.randomBytes(24).toString('hex');

    const origins = [...fileData.origins];
    for (const o of cli.origins) {
        if (!origins.includes(o)) origins.push(o);
    }

    cachedAuth = { token, origins, mtimeMs: Date.now() };
    try {
        fs.writeFileSync(AUTH_FILE, JSON.stringify({ token, origins }, null, 2), { mode: 0o600 });
        const stat = fs.statSync(AUTH_FILE);
        cachedAuth.mtimeMs = stat.mtimeMs;
    } catch {
        // если каталог только для чтения – работаем с секретом в памяти
    }
}

function getAuthState() {
    try {
        if (fs.existsSync(AUTH_FILE)) {
            const stat = fs.statSync(AUTH_FILE);
            if (stat.mtimeMs !== cachedAuth.mtimeMs) {
                const parsed = JSON.parse(fs.readFileSync(AUTH_FILE, 'utf8'));
                if (parsed && typeof parsed.token === 'string' && parsed.token.trim().length >= 16) {
                    const origins = [];
                    if (Array.isArray(parsed.origins)) {
                        for (const o of parsed.origins) {
                            const norm = normalizeOrigin(o);
                            if (norm && !origins.includes(norm)) origins.push(norm);
                        }
                    }
                    cachedAuth = {
                        token: parsed.token.trim(),
                        origins,
                        mtimeMs: stat.mtimeMs,
                    };
                }
            }
        }
    } catch {
        // оставляем текущее состояние в памяти
    }
    if (!cachedAuth.token) {
        initAuthFromCli(process.argv.slice(2));
    }
    return cachedAuth;
}

function isValidToken(candidate) {
    if (typeof candidate !== 'string') return false;
    const trimmed = candidate.trim();
    if (trimmed.length < 16 || trimmed.length > 256) return false;
    const expected = getAuthState().token;
    if (!expected) return false;
    const a = Buffer.from(trimmed, 'utf8');
    const b = Buffer.from(expected, 'utf8');
    return a.length === b.length && crypto.timingSafeEqual(a, b);
}

function extractRequestToken(req, url) {
    const headerToken = req.headers['x-banner-mcp-token'];
    if (typeof headerToken === 'string' && headerToken.trim()) return headerToken.trim();
    const auth = req.headers['authorization'];
    if (typeof auth === 'string' && /^Bearer\s+/i.test(auth)) {
        return auth.replace(/^Bearer\s+/i, '').trim();
    }
    if (url && url.searchParams) {
        const q = url.searchParams.get('token');
        if (q) return q.trim();
    }
    return '';
}

function isAllowedHost(hostHeader) {
    if (!hostHeader || typeof hostHeader !== 'string') return false;
    const h = hostHeader.trim().toLowerCase();
    return h === '127.0.0.1' ||
        h === 'localhost' ||
        h === '[::1]' ||
        h === `127.0.0.1:${PORT}` ||
        h === `localhost:${PORT}` ||
        h === `[::1]:${PORT}`;
}

const TOOLS = [
    {
        name: 'list_templates',
        description: 'Список всех шаблонов (профилей стиля) на сайте генератора баннеров и какой из них сейчас активен.',
        inputSchema: {
            type: 'object',
            properties: {},
            additionalProperties: false,
        },
    },
    {
        name: 'get_template',
        description: 'Получить полный JSON шаблона (параметры выпуска spec и все 10 секций стиля BannerStyle) по id или имени. Если не указаны – возвращает активный шаблон.',
        inputSchema: {
            type: 'object',
            properties: {
                id: { type: 'string', description: 'ID шаблона (необязательно)' },
                name: { type: 'string', description: 'Имя шаблона (необязательно)' },
                includePreviewPng: {
                    type: 'boolean',
                    description: 'Приложить ли отрендеренный PNG баннера (по умолчанию false)'
                },
            },
            additionalProperties: false,
        },
    },
    {
        name: 'get_style_guide',
        description: 'Справочник всех секций и ручек стиля BannerStyle (палитра, раскладка, 3D-текст, плашка, подмазка, полоса, акценты, третья строка, лого, фон), форматов цветов и дефолтных значений.',
        inputSchema: {
            type: 'object',
            properties: {},
            additionalProperties: false,
        },
    },
    {
        name: 'create_template',
        description: 'Создать новый шаблон (профиль стиля) на сайте, опционально клонировав существующий и применив параметры выпуска (spec) и стиля (style). Сразу перерисовывает превью на сайте и возвращает картинку PNG.',
        inputSchema: {
            type: 'object',
            properties: {
                name: { type: 'string', description: 'Имя нового шаблона' },
                baseTemplateId: {
                    type: 'string',
                    description: 'ID шаблона-основы для копирования (если не задан – дефолтный стиль)'
                },
                baseTemplateName: {
                    type: 'string',
                    description: 'Имя шаблона-основы для копирования (если не задан – дефолтный стиль)'
                },
                activate: {
                    type: 'boolean',
                    description: 'Сделать созданный шаблон активным на экране (по умолчанию true)'
                },
                includePreviewPng: { type: 'boolean', description: 'Вернуть отрендеренный PNG (по умолчанию true)' },
                spec: {
                    type: 'object',
                    description: 'Параметры выпуска (можно передать только меняемые поля)',
                    properties: {
                        title: {
                            type: 'string',
                            description: 'Заголовок (ведущий номер вида "290." рисуется зелёным 3D)'
                        },
                        subtitle: { type: 'string', description: 'Подзаголовок на полосе' },
                        tagline: { type: 'string', description: 'Третья строка под подзаголовком' },
                        seed: { type: 'integer', description: 'Seed процедурного гранжа' },
                        width: { type: 'integer', description: 'Ширина кадра (320..3840)' },
                        height: { type: 'integer', description: 'Высота кадра (180..2160)' },
                        transparent: { type: 'boolean', description: 'Прозрачный фон вместо подложки-градиента' },
                    },
                },
                style: {
                    type: 'object',
                    description: 'Частичный или полный объект BannerStyle (palette, layout, titleFx, plate, smear, band, accents, tagline, logo, background). Переданные поля сливаются с базовым стилем.',
                },
            },
            additionalProperties: false,
        },
    },
    {
        name: 'update_template',
        description: 'Отредактировать существующий или активный шаблон. Поддерживает частичный патч: в spec и style достаточно передать только изменяемые секции и поля, остальные сохранятся. Мгновенно обновляет превью на сайте и возвращает новый PNG.',
        inputSchema: {
            type: 'object',
            properties: {
                id: { type: 'string', description: 'ID шаблона (по умолчанию – активный)' },
                name: { type: 'string', description: 'Имя шаблона (если ищем по имени)' },
                newName: { type: 'string', description: 'Новое имя шаблона, если нужно переименовать' },
                summary: { type: 'string', description: 'Краткое описание правки для истории шагов (необязательно)' },
                activate: {
                    type: 'boolean',
                    description: 'Переключить экран сайта на этот шаблон (по умолчанию true)'
                },
                includePreviewPng: { type: 'boolean', description: 'Вернуть отрендеренный PNG (по умолчанию true)' },
                spec: {
                    type: 'object',
                    description: 'Изменяемые параметры выпуска (title, subtitle, tagline, seed, width, height, transparent)',
                    properties: {
                        title: { type: 'string' },
                        subtitle: { type: 'string' },
                        tagline: { type: 'string' },
                        seed: { type: 'integer' },
                        width: { type: 'integer' },
                        height: { type: 'integer' },
                        transparent: { type: 'boolean' },
                    },
                },
                style: {
                    type: 'object',
                    description: 'Изменяемые секции и ручки BannerStyle (palette, layout, titleFx, plate, smear, band, accents, tagline, logo, background). Цвета в формате #RRGGBB или #RRGGBBAA (альфа в конце).',
                },
            },
            additionalProperties: false,
        },
    },
    {
        name: 'duplicate_template',
        description: 'Дублировать существующий (или активный) шаблон под новым именем.',
        inputSchema: {
            type: 'object',
            properties: {
                id: { type: 'string', description: 'ID исходного шаблона (по умолчанию – активный)' },
                name: { type: 'string', description: 'Имя исходного шаблона' },
                newName: { type: 'string', description: 'Имя копии' },
                activate: { type: 'boolean', description: 'Сделать копию активной (по умолчанию true)' },
            },
            additionalProperties: false,
        },
    },
    {
        name: 'rename_template',
        description: 'Переименовать шаблон по id/имени (или текущий активный).',
        inputSchema: {
            type: 'object',
            properties: {
                id: { type: 'string', description: 'ID шаблона (по умолчанию – активный)' },
                name: { type: 'string', description: 'Текущее имя шаблона' },
                newName: { type: 'string', description: 'Новое имя шаблона' },
            },
            required: ['newName'],
            additionalProperties: false,
        },
    },
    {
        name: 'delete_template',
        description: 'Удалить шаблон по id или имени (последний оставшийся шаблон удалить нельзя).',
        inputSchema: {
            type: 'object',
            properties: {
                id: { type: 'string', description: 'ID шаблона' },
                name: { type: 'string', description: 'Имя шаблона' },
            },
            additionalProperties: false,
        },
    },
    {
        name: 'set_active_template',
        description: 'Переключить активный шаблон на сайте по id или имени.',
        inputSchema: {
            type: 'object',
            properties: {
                id: { type: 'string', description: 'ID шаблона' },
                name: { type: 'string', description: 'Имя шаблона' },
                includePreviewPng: {
                    type: 'boolean',
                    description: 'Вернуть отрендеренный PNG выбранного шаблона (по умолчанию true)'
                },
            },
            additionalProperties: false,
        },
    },
    {
        name: 'undo_template',
        description: 'Откатить последние изменения шаблона по истории шагов (на 1 или несколько шагов назад) с перерисовкой в браузере и возвратом PNG.',
        inputSchema: {
            type: 'object',
            properties: {
                id: { type: 'string', description: 'ID шаблона (по умолчанию – активный)' },
                name: { type: 'string', description: 'Имя шаблона' },
                steps: { type: 'integer', description: 'На сколько шагов назад откатить (по умолчанию 1)' },
                includePreviewPng: {
                    type: 'boolean',
                    description: 'Вернуть отрендеренный PNG после отката (по умолчанию true)'
                },
            },
            additionalProperties: false,
        },
    },
    {
        name: 'reset_template_style',
        description: 'Сбросить стиль шаблона к дефолтному BannerStyle, сохранив его тексты и размеры.',
        inputSchema: {
            type: 'object',
            properties: {
                id: { type: 'string', description: 'ID шаблона (по умолчанию – активный)' },
                name: { type: 'string', description: 'Имя шаблона' },
                includePreviewPng: { type: 'boolean', description: 'Вернуть отрендеренный PNG (по умолчанию true)' },
            },
            additionalProperties: false,
        },
    },
    {
        name: 'render_banner',
        description: 'Отрендерить PNG баннера для шаблона (с необязательными временными переопределениями spec/style без сохранения) и получить изображение PNG.',
        inputSchema: {
            type: 'object',
            properties: {
                id: { type: 'string', description: 'ID шаблона (по умолчанию – активный)' },
                name: { type: 'string', description: 'Имя шаблона' },
                spec: { type: 'object', description: 'Временные переопределения параметров выпуска' },
                style: { type: 'object', description: 'Временные переопределения секций стиля' },
            },
            additionalProperties: false,
        },
    },
];

// --- Набор подключённых WebSocket-клиентов браузера ---
const clients = new Set();
const pendingRequests = new Map();
let nextReqId = 1;

function encodeWsFrame(opcode, payloadBuffer) {
    const len = payloadBuffer.length;
    let header;
    if (len < 126) {
        header = Buffer.alloc(2);
        header[0] = 0x80 | opcode;
        header[1] = len;
    } else if (len < 65536) {
        header = Buffer.alloc(4);
        header[0] = 0x80 | opcode;
        header[1] = 126;
        header.writeUInt16BE(len, 2);
    } else {
        header = Buffer.alloc(10);
        header[0] = 0x80 | opcode;
        header[1] = 127;
        header.writeBigUInt64BE(BigInt(len), 2);
    }
    return Buffer.concat([header, payloadBuffer]);
}

function sendWsText(socket, text) {
    if (socket.destroyed) return;
    socket.write(encodeWsFrame(0x01, Buffer.from(text, 'utf8')));
}

function attachWsClient(socket) {
    if (clients.size >= MAX_WS_CLIENTS) {
        for (const c of clients) {
            if (!c.verified) {
                c.socket.destroy();
                clients.delete(c);
                break;
            }
        }
        if (clients.size >= MAX_WS_CLIENTS) {
            socket.destroy();
            return;
        }
    }

    const client = {
        socket,
        buffer: Buffer.alloc(0),
        fragments: [],
        fragmentsBytes: 0,
        connectedAt: new Date().toISOString(),
        verified: false,
        info: null,
        helloTimer: setTimeout(() => {
            if (!client.verified && !socket.destroyed) {
                socket.destroy();
            }
        }, 5000),
    };
    clients.add(client);

    socket.on('data', chunk => {
        try {
            const maxAllowed = client.verified ? MAX_WS_MESSAGE_BYTES : MAX_WS_HELLO_BYTES;
            if (client.buffer.length + chunk.length > maxAllowed + 14) {
                socket.destroy();
                return;
            }
            client.buffer = Buffer.concat([client.buffer, chunk]);
            while (client.buffer.length >= 2) {
                const b0 = client.buffer[0];
                const b1 = client.buffer[1];
                if ((b0 & 0x70) !== 0) {
                    socket.destroy();
                    return;
                }
                const fin = (b0 & 0x80) !== 0;
                const opcode = b0 & 0x0f;
                const masked = (b1 & 0x80) !== 0;
                if (!masked) {
                    socket.destroy();
                    return;
                }
                let payloadLen = b1 & 0x7f;
                let offset = 2;

                if (payloadLen === 126) {
                    if (client.buffer.length < 4) return;
                    payloadLen = client.buffer.readUInt16BE(2);
                    offset = 4;
                } else if (payloadLen === 127) {
                    if (client.buffer.length < 10) return;
                    const bigLen = client.buffer.readBigUInt64BE(2);
                    if (bigLen > BigInt(maxAllowed)) {
                        socket.destroy();
                        return;
                    }
                    payloadLen = Number(bigLen);
                    offset = 10;
                }

                const frameLimit = client.verified ? MAX_WS_MESSAGE_BYTES : MAX_WS_HELLO_BYTES;
                if (payloadLen > frameLimit) {
                    socket.destroy();
                    return;
                }

                const maskLen = 4;
                if (client.buffer.length < offset + maskLen + payloadLen) return;

                const maskKey = client.buffer.subarray(offset, offset + 4);
                offset += maskLen;
                const payload = Buffer.from(client.buffer.subarray(offset, offset + payloadLen));
                client.buffer = client.buffer.subarray(offset + payloadLen);

                for (let i = 0; i < payload.length; i++) {
                    payload[i] ^= maskKey[i & 3];
                }

                if (opcode === 0x08) {
                    socket.end(encodeWsFrame(0x08, Buffer.alloc(0)));
                    return;
                }
                if (opcode === 0x09) {
                    if (payload.length > 125) {
                        socket.destroy();
                        return;
                    }
                    socket.write(encodeWsFrame(0x0a, payload));
                    continue;
                }
                if (opcode === 0x0a) {
                    continue;
                }

                if (opcode === 0x01 || opcode === 0x00) {
                    if (
                        client.fragments.length >= MAX_WS_FRAGMENTS ||
                        client.fragmentsBytes + payload.length > frameLimit
                    ) {
                        socket.destroy();
                        return;
                    }
                    client.fragments.push(payload);
                    client.fragmentsBytes += payload.length;
                    if (fin) {
                        const full = Buffer.concat(client.fragments).toString('utf8');
                        client.fragments = [];
                        client.fragmentsBytes = 0;
                        handleWsMessage(client, full);
                    }
                } else {
                    socket.destroy();
                    return;
                }
            }
        } catch {
            socket.destroy();
        }
    });

    const cleanup = () => {
        if (client.helloTimer) {
            clearTimeout(client.helloTimer);
            client.helloTimer = null;
        }
        clients.delete(client);
        for (const [id, pending] of pendingRequests.entries()) {
            if (pending.client === client) {
                pendingRequests.delete(id);
                clearTimeout(pending.timer);
                pending.reject(new Error('Соединение с вкладкой браузера разорвано до получения ответа'));
            }
        }
    };
    socket.on('close', cleanup);
    socket.on('error', cleanup);
}

function handleWsMessage(client, raw) {
    let msg;
    try {
        msg = JSON.parse(raw);
    } catch {
        return;
    }

    if (!msg || typeof msg !== 'object' || Array.isArray(msg)) {
        return;
    }

    if (msg.type === 'hello') {
        if (msg.app !== 'procedural-banner-generator' || !isValidToken(msg.token)) {
            client.socket.destroy();
            return;
        }
        if (typeof msg.url === 'string' && msg.url.length > 0 && !isAllowedBrowserOrigin(msg.url)) {
            client.socket.destroy();
            return;
        }
        client.verified = true;
        if (client.helloTimer) {
            clearTimeout(client.helloTimer);
            client.helloTimer = null;
        }
        client.info = {
            type: 'hello',
            app: msg.app,
            url: typeof msg.url === 'string' ? msg.url.slice(0, 512) : '',
        };
        return;
    }

    if (!client.verified) {
        client.socket.destroy();
        return;
    }

    if (typeof msg.id === 'string' && pendingRequests.has(msg.id)) {
        const pending = pendingRequests.get(msg.id);
        if (pending.client !== client) {
            return;
        }
        pendingRequests.delete(msg.id);
        clearTimeout(pending.timer);
        if (msg.error) {
            pending.reject(new Error(String(msg.error)));
        } else {
            pending.resolve(msg.result);
        }
    }
}

async function waitForActiveClient(timeoutMs = 4000) {
    const pick = () => {
        let last = null;
        for (const c of clients) {
            if (!c.socket.destroyed && c.verified) last = c;
        }
        return last;
    };

    const existing = pick();
    if (existing) return existing;

    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        await new Promise(r => setTimeout(r, 150));
        const c = pick();
        if (c) return c;
    }
    return null;
}

async function callBrowserToolLocal(tool, args) {
    const client = await waitForActiveClient(4000);
    if (!client) {
        throw new Error(
            `Нет подключённой вкладки генератора баннеров (мост слушает ws://${HOST}:${PORT}/ws). ` +
            'Откройте сайт генератора в браузере – вкладка подключится автоматически.'
        );
    }

    const id = `req-${nextReqId++}-${Date.now()}`;
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => {
            pendingRequests.delete(id);
            reject(new Error(`Таймаут ответа от вкладки браузера при вызове ${tool}`));
        }, 25000);

        pendingRequests.set(id, { resolve, reject, timer, client });
        sendWsText(client.socket, JSON.stringify({ id, tool, args: args || {} }));
    });
}

function buildMcpHttpUrl() {
    const token = getAuthState().token;
    return `http://${HOST}:${PORT}/mcp?token=${encodeURIComponent(token)}`;
}

function httpJsonRequest(method, urlPath, bodyObj, timeoutMs = 28000) {
    return new Promise((resolve, reject) => {
        const payload = bodyObj ? Buffer.from(JSON.stringify(bodyObj), 'utf8') : null;
        const headers = {
            'X-Banner-Mcp-Token': getAuthState().token,
        };
        if (payload) {
            headers['Content-Type'] = 'application/json';
            headers['Content-Length'] = payload.length;
        }
        const req = http.request(
            {
                hostname: HOST,
                port: PORT,
                path: urlPath,
                method,
                headers,
                timeout: timeoutMs,
            },
            res => {
                const chunks = [];
                res.on('data', c => chunks.push(c));
                res.on('end', () => {
                    const text = Buffer.concat(chunks).toString('utf8');
                    try {
                        resolve({ status: res.statusCode, data: JSON.parse(text) });
                    } catch {
                        resolve({ status: res.statusCode, data: text });
                    }
                });
            }
        );
        req.on('timeout', () => req.destroy(new Error('HTTP timeout')));
        req.on('error', reject);
        if (payload) req.write(payload);
        req.end();
    });
}

let isHubOwner = false;

async function invokeTool(tool, args) {
    if (isHubOwner) {
        return await callBrowserToolLocal(tool, args);
    }
    const resp = await httpJsonRequest('POST', '/rpc', { tool, args: args || {} });
    if (!resp.data || !resp.data.ok) {
        throw new Error(resp.data && resp.data.error || `Ошибка вызова ${tool} через локальный мост`);
    }
    return resp.data.result;
}

function formatMcpToolResult(rawResult) {
    const data = typeof rawResult === 'string' ? JSON.parse(rawResult) : { ...rawResult };
    const content = [];
    let pngBase64 = null;

    if (data && typeof data === 'object' && typeof data.previewPngBase64 === 'string' && data.previewPngBase64.length > 0) {
        pngBase64 = data.previewPngBase64;
        delete data.previewPngBase64;
        data.previewPngIncluded = true;
    }

    content.push({
        type: 'text',
        text: JSON.stringify(data, null, 2),
    });

    if (pngBase64) {
        content.push({
            type: 'image',
            data: pngBase64,
            mimeType: 'image/png',
        });
    }

    return { content };
}

async function handleMcpJsonRpc(msg) {
    if (!msg || typeof msg !== 'object' || Array.isArray(msg)) return null;

    const { id, method, params } = msg;
    if (id === undefined || id === null) {
        // Уведомление (например, notifications/initialized) – ответа не требует
        return null;
    }

    try {
        if (method === 'initialize') {
            const clientVersion = params && params.protocolVersion || '2024-11-05';
            return {
                jsonrpc: '2.0',
                id,
                result: {
                    protocolVersion: clientVersion,
                    capabilities: {
                        tools: {},
                    },
                    serverInfo: {
                        name: 'procedural-banner-generator',
                        version: '1.0.0',
                    },
                },
            };
        }

        if (method === 'ping') {
            return { jsonrpc: '2.0', id, result: {} };
        }

        if (method === 'tools/list') {
            return {
                jsonrpc: '2.0',
                id,
                result: {
                    tools: TOOLS,
                },
            };
        }

        if (method === 'tools/call') {
            const toolName = params && params.name;
            const toolArgs = params && params.arguments || {};
            if (!toolName || !TOOLS.some(t => t.name === toolName)) {
                return {
                    jsonrpc: '2.0',
                    id,
                    error: { code: -32602, message: `Неизвестный инструмент: ${toolName}` },
                };
            }

            try {
                const rawResult = await invokeTool(toolName, toolArgs);
                return {
                    jsonrpc: '2.0',
                    id,
                    result: formatMcpToolResult(rawResult),
                };
            } catch (toolErr) {
                return {
                    jsonrpc: '2.0',
                    id,
                    result: {
                        isError: true,
                        content: [{ type: 'text', text: String(toolErr.message || toolErr) }],
                    },
                };
            }
        }

        if (method === 'resources/list') {
            return { jsonrpc: '2.0', id, result: { resources: [] } };
        }

        if (method === 'prompts/list') {
            return { jsonrpc: '2.0', id, result: { prompts: [] } };
        }

        return {
            jsonrpc: '2.0',
            id,
            error: { code: -32601, message: `Метод не поддерживается: ${method}` },
        };
    } catch (err) {
        return {
            jsonrpc: '2.0',
            id,
            error: { code: -32603, message: String(err.message || err) },
        };
    }
}

function isAllowedBrowserOrigin(origin) {
    if (!origin) return false;
    const norm = normalizeOrigin(origin);
    if (!norm) return false;
    if (DEFAULT_ALLOWED_ORIGINS.has(norm)) return true;
    return getAuthState().origins.includes(norm);
}

function setCorsHeaders(req, res) {
    const origin = req.headers.origin;
    if (origin && isAllowedBrowserOrigin(origin)) {
        res.setHeader('Access-Control-Allow-Origin', normalizeOrigin(origin));
        res.setHeader('Vary', 'Origin');
        res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
        res.setHeader(
            'Access-Control-Allow-Headers',
            'Content-Type, Accept, Mcp-Session-Id, Authorization, X-Banner-Mcp-Token'
        );
        res.setHeader('Access-Control-Allow-Private-Network', 'true');
    }
}

function readHttpBody(req, maxBytes = MAX_HTTP_BODY_BYTES) {
    return new Promise((resolve, reject) => {
        const chunks = [];
        let total = 0;
        let tooLarge = false;
        req.on('data', c => {
            if (tooLarge) return;
            total += c.length;
            if (total > maxBytes) {
                tooLarge = true;
                const err = new Error(`Превышен лимит размера HTTP-тела (${maxBytes} байт)`);
                err.statusCode = 413;
                reject(err);
                return;
            }
            chunks.push(c);
        });
        req.on('end', () => {
            if (!tooLarge) resolve(Buffer.concat(chunks).toString('utf8'));
        });
        req.on('error', reject);
    });
}

function startHubServer() {
    return new Promise(resolve => {
        const server = http.createServer(async (req, res) => {
            if (!isAllowedHost(req.headers.host)) {
                res.writeHead(403, { 'Content-Type': 'application/json; charset=utf-8' });
                res.end(JSON.stringify({ error: 'Forbidden host' }));
                return;
            }

            if (req.headers.origin && !isAllowedBrowserOrigin(req.headers.origin)) {
                res.writeHead(403, { 'Content-Type': 'application/json; charset=utf-8' });
                res.end(JSON.stringify({ error: 'Forbidden origin' }));
                return;
            }

            setCorsHeaders(req, res);
            if (req.method === 'OPTIONS') {
                res.writeHead(204);
                res.end();
                return;
            }

            const url = new URL(req.url || '/', `http://${HOST}:${PORT}`);
            const paired = isValidToken(extractRequestToken(req, url));

            if (req.method === 'GET' && url.pathname === '/health') {
                if (!paired) {
                    res.writeHead(401, { 'Content-Type': 'application/json; charset=utf-8' });
                    res.end(JSON.stringify({ ok: false, error: 'Unauthorized' }));
                    return;
                }
                const active = [...clients].filter(c => !c.socket.destroyed && c.verified);
                res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
                res.end(
                    JSON.stringify({
                        ok: true,
                        paired: true,
                        port: PORT,
                        mcpHttpUrl: buildMcpHttpUrl(),
                        nodePath: process.execPath,
                        scriptPath: __filename,
                        connectedClients: active.length,
                        activeClient: active.length > 0 ? active[active.length - 1].info : null,
                    })
                );
                return;
            }

            if (!paired) {
                res.writeHead(401, { 'Content-Type': 'application/json; charset=utf-8' });
                res.end(JSON.stringify({ error: 'Unauthorized: missing or invalid MCP pairing token' }));
                return;
            }

            if (req.method === 'POST' && url.pathname === '/rpc') {
                try {
                    const rawBody = await readHttpBody(req);
                    const body = JSON.parse(rawBody || '{}');
                    if (!body || typeof body !== 'object' || Array.isArray(body)) {
                        res.writeHead(400, { 'Content-Type': 'application/json; charset=utf-8' });
                        res.end(JSON.stringify({ ok: false, error: 'Ожидался JSON-объект' }));
                        return;
                    }
                    const result = await callBrowserToolLocal(body.tool, body.args || {});
                    const parsed = typeof result === 'string' ? JSON.parse(result) : result;
                    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
                    res.end(JSON.stringify({ ok: true, result: parsed }));
                } catch (err) {
                    const status = err && err.statusCode === 413 ? 413 : 200;
                    res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8' });
                    res.end(JSON.stringify({ ok: false, error: String(err && err.message || err) }));
                }
                return;
            }

            if (req.method === 'POST' && url.pathname === '/mcp') {
                try {
                    const rawBody = await readHttpBody(req);
                    const body = JSON.parse(rawBody || '{}');
                    const reply = await handleMcpJsonRpc(body);
                    if (!reply) {
                        res.writeHead(202);
                        res.end();
                        return;
                    }
                    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
                    res.end(JSON.stringify(reply));
                } catch (err) {
                    const status = err && err.statusCode === 413 ? 413 : 400;
                    const code = status === 413 ? -32600 : -32700;
                    res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8' });
                    res.end(
                        JSON.stringify({
                            jsonrpc: '2.0',
                            id: null,
                            error: { code, message: String(err && err.message || err) },
                        })
                    );
                }
                return;
            }

            res.writeHead(404, { 'Content-Type': 'application/json; charset=utf-8' });
            res.end(JSON.stringify({ error: 'Not found' }));
        });

        server.headersTimeout = 10000;
        server.requestTimeout = 30000;
        server.keepAliveTimeout = 5000;

        server.on('upgrade', (req, socket) => {
            if (!isAllowedHost(req.headers.host)) {
                socket.destroy();
                return;
            }
            const url = new URL(req.url || '/', `http://${HOST}:${PORT}`);
            const key = req.headers['sec-websocket-key'];
            if (
                !key ||
                url.pathname !== '/ws' ||
                req.headers.origin && !isAllowedBrowserOrigin(req.headers.origin) ||
                !isValidToken(extractRequestToken(req, url))
            ) {
                socket.destroy();
                return;
            }
            const accept = crypto.createHash('sha1').update(key + WS_GUID).digest('base64');
            const headers = [
                'HTTP/1.1 101 Switching Protocols',
                'Upgrade: websocket',
                'Connection: Upgrade',
                `Sec-WebSocket-Accept: ${accept}`,
            ];
            if (req.headers.origin) {
                headers.push(`Access-Control-Allow-Origin: ${normalizeOrigin(req.headers.origin)}`);
                headers.push('Access-Control-Allow-Private-Network: true');
            }
            headers.push('\r\n');
            socket.write(headers.join('\r\n'));
            attachWsClient(socket);
        });

        server.on('error', err => {
            if (err && err.code === 'EADDRINUSE') {
                isHubOwner = false;
                resolve({ started: false, server: null });
            } else {
                isHubOwner = false;
                resolve({ started: false, server: null, error: err });
            }
        });

        server.listen(PORT, HOST, () => {
            isHubOwner = true;
            resolve({ started: true, server });
        });
    });
}

function startStdioMcpLoop() {
    let buffer = Buffer.alloc(0);
    let useContentLength = false;

    const writeReply = obj => {
        if (!obj) return;
        const json = JSON.stringify(obj);
        if (useContentLength) {
            const byteLen = Buffer.byteLength(json, 'utf8');
            process.stdout.write(`Content-Length: ${byteLen}\r\n\r\n${json}`);
        } else {
            process.stdout.write(json + '\n');
        }
    };

    process.stdin.on('data', async chunk => {
        if (buffer.length + chunk.length > MAX_STDIO_MESSAGE_BYTES) {
            buffer = Buffer.alloc(0);
            writeReply({ jsonrpc: '2.0', id: null, error: { code: -32600, message: 'Message too large' } });
            return;
        }
        buffer = Buffer.concat([buffer, chunk]);

        while (buffer.length > 0) {
            const preview = buffer.subarray(0, Math.min(buffer.length, 32)).toString('utf8');
            if (preview.startsWith('Content-Length:')) {
                useContentLength = true;
                const headerEnd = buffer.indexOf('\r\n\r\n');
                if (headerEnd < 0) return;
                const headerText = buffer.subarray(0, headerEnd).toString('utf8');
                const match = /Content-Length:\s*(\d+)/i.exec(headerText);
                if (!match) {
                    buffer = buffer.subarray(headerEnd + 4);
                    continue;
                }
                const bodyLen = Number(match[1]);
                if (!Number.isSafeInteger(bodyLen) || bodyLen < 0 || bodyLen > MAX_STDIO_MESSAGE_BYTES) {
                    buffer = Buffer.alloc(0);
                    writeReply({ jsonrpc: '2.0', id: null, error: { code: -32600, message: 'Message too large' } });
                    return;
                }
                const totalLen = headerEnd + 4 + bodyLen;
                if (buffer.length < totalLen) return;
                const bodyText = buffer.subarray(headerEnd + 4, totalLen).toString('utf8');
                buffer = buffer.subarray(totalLen);
                let msg;
                try {
                    msg = JSON.parse(bodyText);
                } catch {
                    writeReply({ jsonrpc: '2.0', id: null, error: { code: -32700, message: 'Parse error' } });
                    continue;
                }
                const reply = await handleMcpJsonRpc(msg);
                writeReply(reply);
            } else {
                const nl = buffer.indexOf(0x0a);
                if (nl < 0) return;
                const line = buffer.subarray(0, nl).toString('utf8').trim();
                buffer = buffer.subarray(nl + 1);
                if (!line) continue;
                let msg;
                try {
                    msg = JSON.parse(line);
                } catch {
                    writeReply({ jsonrpc: '2.0', id: null, error: { code: -32700, message: 'Parse error' } });
                    continue;
                }
                const reply = await handleMcpJsonRpc(msg);
                writeReply(reply);
            }
        }
    });
}

async function ensureDaemonRunning() {
    try {
        const health = await httpJsonRequest('GET', '/health', null, 1200);
        if (health.status === 200 && health.data && health.data.ok) {
            return health.data;
        }
    } catch {
        // мост ещё не запущен
    }

    const child = spawn(process.execPath, [__filename, '--serve'], {
        detached: true,
        stdio: 'ignore',
    });
    child.unref();

    const deadline = Date.now() + 4000;
    while (Date.now() < deadline) {
        await new Promise(r => setTimeout(r, 150));
        try {
            const health = await httpJsonRequest('GET', '/health', null, 1000);
            if (health.status === 200 && health.data && health.data.ok) {
                return health.data;
            }
        } catch {
            // ждём подъёма
        }
    }
    throw new Error(`Не удалось запустить фоновый мост на ${HOST}:${PORT}`);
}

async function main() {
    const args = process.argv.slice(2);
    initAuthFromCli(args);
    const cmd = args[0] || '';

    if (cmd === '--serve') {
        const { started, error } = await startHubServer();
        if (!started) {
            if (error) console.error(error);
            process.exit(0);
        }
        // Держим процесс живым как WebSocket/HTTP-хаб
        setInterval(() => {
        }, 60000);
        return;
    }

    if (cmd === '--daemon') {
        const status = await ensureDaemonRunning();
        // Если вкладка уже открыта – дадим ей ~1.5 с на авто-реконнект к только что поднятому мосту
        if (status.connectedClients === 0) {
            const waitEnd = Date.now() + 2000;
            while (Date.now() < waitEnd) {
                await new Promise(r => setTimeout(r, 250));
                const check = await httpJsonRequest('GET', '/health', null, 1000).catch(() => null);
                if (check && check.data && check.data.connectedClients > 0) {
                    console.log(
                        JSON.stringify(
                            {
                                ...check.data,
                                mcpHttpUrl: buildMcpHttpUrl(),
                                nodePath: process.execPath,
                                scriptPath: __filename,
                            },
                            null,
                            2
                        )
                    );
                    return;
                }
            }
        }
        const finalCheck = await httpJsonRequest('GET', '/health', null, 1000).catch(() => ({ data: status }));
        console.log(
            JSON.stringify(
                {
                    ...(finalCheck.data || {}),
                    mcpHttpUrl: buildMcpHttpUrl(),
                    nodePath: process.execPath,
                    scriptPath: __filename,
                },
                null,
                2
            )
        );
        return;
    }

    if (cmd === 'status') {
        try {
            const health = await httpJsonRequest('GET', '/health', null, 1500);
            console.log(
                JSON.stringify(
                    {
                        ...(health.data || {}),
                        mcpHttpUrl: buildMcpHttpUrl(),
                        nodePath: process.execPath,
                        scriptPath: __filename,
                    },
                    null,
                    2
                )
            );
        } catch {
            console.log(
                JSON.stringify(
                    {
                        ok: false,
                        running: false,
                        port: PORT,
                        mcpHttpUrl: buildMcpHttpUrl(),
                        nodePath: process.execPath,
                        scriptPath: __filename,
                    },
                    null,
                    2
                )
            );
        }
        return;
    }

    if (cmd === 'call') {
        const toolName = args[1];
        if (!toolName) {
            console.error(
                'Использование: node mcp-bridge.js call <tool> [jsonArgs|@args.json] [--args-file args.json] [--stdin] [--out banner.png]'
            );
            process.exit(1);
        }
        let jsonArgs = '{}';
        let argsFile = null;
        let useStdin = false;
        let outPath = null;
        for (let i = 2; i < args.length; i++) {
            if (args[i] === '--out' && i + 1 < args.length) {
                outPath = args[++i];
            } else if (args[i] === '--args-file' && i + 1 < args.length) {
                argsFile = args[++i];
            } else if ((args[i] === '--token' || args[i] === '--origin') && i + 1 < args.length) {
                i++;
            } else if (args[i] === '--stdin') {
                useStdin = true;
            } else if (!args[i].startsWith('--')) {
                jsonArgs = args[i];
            }
        }

        if (useStdin) {
            jsonArgs = fs.readFileSync(0, 'utf8').replace(/^\uFEFF/, '');
        } else if (argsFile) {
            jsonArgs = fs.readFileSync(path.resolve(argsFile), 'utf8').replace(/^\uFEFF/, '');
        } else if (jsonArgs.startsWith('@') && jsonArgs.length > 1) {
            jsonArgs = fs.readFileSync(path.resolve(jsonArgs.slice(1)), 'utf8').replace(/^\uFEFF/, '');
        }

        await ensureDaemonRunning();
        let parsedArgs;
        try {
            parsedArgs = JSON.parse(jsonArgs);
        } catch (err) {
            if (!useStdin && !argsFile && fs.existsSync(jsonArgs)) {
                parsedArgs = JSON.parse(fs.readFileSync(path.resolve(jsonArgs), 'utf8').replace(/^\uFEFF/, ''));
            } else {
                throw err;
            }
        }
        const result = await invokeTool(toolName, parsedArgs);
        const obj = typeof result === 'string' ? JSON.parse(result) : result;

        if (obj && typeof obj.previewPngBase64 === 'string' && obj.previewPngBase64.length > 0) {
            const pngBytes = Buffer.from(obj.previewPngBase64, 'base64');
            const targetFile = outPath
                ? path.resolve(outPath)
                : path.join(os.tmpdir(), 'banner-mcp-preview.png');
            fs.writeFileSync(targetFile, pngBytes);
            obj.previewPngSavedTo = targetFile;
            obj.previewPngBytes = pngBytes.length;
            delete obj.previewPngBase64;
        }

        console.log(JSON.stringify(obj, null, 2));
        return;
    }

    // Стандартный режим MCP (stdio)
    await startHubServer();
    startStdioMcpLoop();
}

main().catch(err => {
    console.error(err);
    process.exit(1);
});
