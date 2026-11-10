#!/usr/bin/env node
// Локальный MCP-мост для процедурного генератора баннеров.
// Работает без внешних npm-зависимостей (только встроенные модули Node.js).
// Режимы запуска:
//   node mcp-bridge.js            – стандартный MCP-сервер (stdio) + WebSocket-мост на 127.0.0.1:5219
//   node mcp-bridge.js --daemon   – запуск фонового моста на 127.0.0.1:5219 (для мгновенного коннекта вкладки)
//   node mcp-bridge.js --serve    – работа моста в текущем процессе (HTTP /mcp, /rpc, /health + WS /ws)
//   node mcp-bridge.js status     – проверка связи с мостом и вкладкой браузера
//   node mcp-bridge.js call <tool> [jsonArgs] [--out banner.png] – прямой вызов инструмента из CLI

'use strict';

const http = require('node:http');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');

const PORT = Number(process.env.BANNER_MCP_PORT || 5219);
const HOST = '127.0.0.1';
const WS_GUID = '258EAFA5-E914-47DA-95CA-C5AB0DC85B11';

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
                includePreviewPng: { type: 'boolean', description: 'Приложить ли отрендеренный PNG баннера (по умолчанию false)' },
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
                baseTemplateId: { type: 'string', description: 'ID шаблона-основы для копирования (если не задан – дефолтный стиль)' },
                baseTemplateName: { type: 'string', description: 'Имя шаблона-основы для копирования (если не задан – дефолтный стиль)' },
                activate: { type: 'boolean', description: 'Сделать созданный шаблон активным на экране (по умолчанию true)' },
                includePreviewPng: { type: 'boolean', description: 'Вернуть отрендеренный PNG (по умолчанию true)' },
                spec: {
                    type: 'object',
                    description: 'Параметры выпуска (можно передать только меняемые поля)',
                    properties: {
                        title: { type: 'string', description: 'Заголовок (ведущий номер вида "290." рисуется зелёным 3D)' },
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
                activate: { type: 'boolean', description: 'Переключить экран сайта на этот шаблон (по умолчанию true)' },
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
                includePreviewPng: { type: 'boolean', description: 'Вернуть отрендеренный PNG выбранного шаблона (по умолчанию true)' },
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
                includePreviewPng: { type: 'boolean', description: 'Вернуть отрендеренный PNG после отката (по умолчанию true)' },
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
    const client = {
        socket,
        buffer: Buffer.alloc(0),
        fragments: [],
        connectedAt: new Date().toISOString(),
        info: null,
    };
    clients.add(client);

    socket.on('data', (chunk) => {
        client.buffer = Buffer.concat([client.buffer, chunk]);
        while (client.buffer.length >= 2) {
            const b0 = client.buffer[0];
            const b1 = client.buffer[1];
            const fin = (b0 & 0x80) !== 0;
            const opcode = b0 & 0x0f;
            const masked = (b1 & 0x80) !== 0;
            let payloadLen = b1 & 0x7f;
            let offset = 2;

            if (payloadLen === 126) {
                if (client.buffer.length < 4) return;
                payloadLen = client.buffer.readUInt16BE(2);
                offset = 4;
            } else if (payloadLen === 127) {
                if (client.buffer.length < 10) return;
                payloadLen = Number(client.buffer.readBigUInt64BE(2));
                offset = 10;
            }

            const maskLen = masked ? 4 : 0;
            if (client.buffer.length < offset + maskLen + payloadLen) return;

            const maskKey = masked ? client.buffer.subarray(offset, offset + 4) : null;
            offset += maskLen;
            const payload = Buffer.from(client.buffer.subarray(offset, offset + payloadLen));
            client.buffer = client.buffer.subarray(offset + payloadLen);

            if (masked && maskKey) {
                for (let i = 0; i < payload.length; i++) {
                    payload[i] ^= maskKey[i & 3];
                }
            }

            if (opcode === 0x08) {
                // Close
                socket.end(encodeWsFrame(0x08, Buffer.alloc(0)));
                return;
            }
            if (opcode === 0x09) {
                // Ping -> Pong
                socket.write(encodeWsFrame(0x0a, payload));
                continue;
            }
            if (opcode === 0x0a) {
                continue;
            }

            if (opcode === 0x01 || opcode === 0x00) {
                client.fragments.push(payload);
                if (fin) {
                    const full = Buffer.concat(client.fragments).toString('utf8');
                    client.fragments = [];
                    handleWsMessage(client, full);
                }
            }
        }
    });

    const cleanup = () => {
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

    if (msg.type === 'hello') {
        client.info = msg;
        return;
    }

    if (msg.id && pendingRequests.has(msg.id)) {
        const pending = pendingRequests.get(msg.id);
        pendingRequests.delete(msg.id);
        clearTimeout(pending.timer);
        if (msg.error) {
            pending.reject(new Error(msg.error));
        } else {
            pending.resolve(msg.result);
        }
    }
}

async function waitForActiveClient(timeoutMs = 4000) {
    const pick = () => {
        let last = null;
        for (const c of clients) {
            if (!c.socket.destroyed) last = c;
        }
        return last;
    };

    const existing = pick();
    if (existing) return existing;

    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        await new Promise((r) => setTimeout(r, 150));
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

function httpJsonRequest(method, urlPath, bodyObj, timeoutMs = 28000) {
    return new Promise((resolve, reject) => {
        const payload = bodyObj ? Buffer.from(JSON.stringify(bodyObj), 'utf8') : null;
        const req = http.request(
            {
                hostname: HOST,
                port: PORT,
                path: urlPath,
                method,
                headers: payload
                    ? {
                          'Content-Type': 'application/json',
                          'Content-Length': payload.length,
                      }
                    : {},
                timeout: timeoutMs,
            },
            (res) => {
                const chunks = [];
                res.on('data', (c) => chunks.push(c));
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
        req.on('timeout', () => {
            req.destroy(new Error('HTTP timeout'));
        });
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
        throw new Error((resp.data && resp.data.error) || `Ошибка вызова ${tool} через локальный мост`);
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
    if (!msg || typeof msg !== 'object') return null;

    const { id, method, params } = msg;
    if (id === undefined || id === null) {
        // Уведомление (например, notifications/initialized) – ответа не требует
        return null;
    }

    try {
        if (method === 'initialize') {
            const clientVersion = (params && params.protocolVersion) || '2024-11-05';
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
            const toolArgs = (params && params.arguments) || {};
            if (!toolName || !TOOLS.some((t) => t.name === toolName)) {
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
    if (!origin) return true;
    if (process.env.BANNER_MCP_ORIGIN && origin === process.env.BANNER_MCP_ORIGIN) return true;
    try {
        const u = new URL(origin);
        return (
            u.hostname === 'localhost' ||
            u.hostname === '127.0.0.1' ||
            u.hostname === '[::1]' ||
            u.hostname === 'thevsakeeper.github.io'
        );
    } catch {
        return false;
    }
}

function setCorsHeaders(req, res) {
    const origin = req.headers.origin;
    if (origin && isAllowedBrowserOrigin(origin)) {
        res.setHeader('Access-Control-Allow-Origin', origin);
        res.setHeader('Vary', 'Origin');
        res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Accept, Mcp-Session-Id');
        res.setHeader('Access-Control-Allow-Private-Network', 'true');
    }
}

function readHttpBody(req) {
    return new Promise((resolve, reject) => {
        const chunks = [];
        req.on('data', (c) => chunks.push(c));
        req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
        req.on('error', reject);
    });
}

function startHubServer() {
    return new Promise((resolve) => {
        const server = http.createServer(async (req, res) => {
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

            if (req.method === 'GET' && url.pathname === '/health') {
                const active = [...clients].filter((c) => !c.socket.destroyed);
                res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
                res.end(
                    JSON.stringify({
                        ok: true,
                        port: PORT,
                        scriptPath: __filename,
                        connectedClients: active.length,
                        activeClient: active.length > 0 ? active[active.length - 1].info : null,
                    })
                );
                return;
            }

            if (req.method === 'POST' && url.pathname === '/rpc') {
                try {
                    const body = JSON.parse((await readHttpBody(req)) || '{}');
                    const result = await callBrowserToolLocal(body.tool, body.args || {});
                    const parsed = typeof result === 'string' ? JSON.parse(result) : result;
                    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
                    res.end(JSON.stringify({ ok: true, result: parsed }));
                } catch (err) {
                    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
                    res.end(JSON.stringify({ ok: false, error: String(err.message || err) }));
                }
                return;
            }

            if (req.method === 'POST' && url.pathname === '/mcp') {
                try {
                    const body = JSON.parse((await readHttpBody(req)) || '{}');
                    const reply = await handleMcpJsonRpc(body);
                    if (!reply) {
                        res.writeHead(202);
                        res.end();
                        return;
                    }
                    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
                    res.end(JSON.stringify(reply));
                } catch (err) {
                    res.writeHead(400, { 'Content-Type': 'application/json; charset=utf-8' });
                    res.end(JSON.stringify({ jsonrpc: '2.0', id: null, error: { code: -32700, message: String(err) } }));
                }
                return;
            }

            res.writeHead(404, { 'Content-Type': 'application/json; charset=utf-8' });
            res.end(JSON.stringify({ error: 'Not found' }));
        });

        server.on('upgrade', (req, socket) => {
            const url = new URL(req.url || '/', `http://${HOST}:${PORT}`);
            const key = req.headers['sec-websocket-key'];
            if (!key || url.pathname !== '/ws' || !isAllowedBrowserOrigin(req.headers.origin)) {
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
                headers.push(`Access-Control-Allow-Origin: ${req.headers.origin}`);
                headers.push('Access-Control-Allow-Private-Network: true');
            }
            headers.push('\r\n');
            socket.write(headers.join('\r\n'));
            attachWsClient(socket);
        });

        server.on('error', (err) => {
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

    const writeReply = (obj) => {
        if (!obj) return;
        const json = JSON.stringify(obj);
        if (useContentLength) {
            const byteLen = Buffer.byteLength(json, 'utf8');
            process.stdout.write(`Content-Length: ${byteLen}\r\n\r\n${json}`);
        } else {
            process.stdout.write(json + '\n');
        }
    };

    process.stdin.on('data', async (chunk) => {
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
        await new Promise((r) => setTimeout(r, 150));
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
    const cmd = args[0] || '';

    if (cmd === '--serve') {
        const { started, error } = await startHubServer();
        if (!started) {
            if (error) console.error(error);
            process.exit(0);
        }
        // Держим процесс живым как WebSocket/HTTP-хаб
        setInterval(() => {}, 60000);
        return;
    }

    if (cmd === '--daemon') {
        const status = await ensureDaemonRunning();
        // Если вкладка уже открыта – дадим ей ~1.5 с на авто-реконнект к только что поднятому мосту
        if (status.connectedClients === 0) {
            const waitEnd = Date.now() + 2000;
            while (Date.now() < waitEnd) {
                await new Promise((r) => setTimeout(r, 250));
                const check = await httpJsonRequest('GET', '/health', null, 1000).catch(() => null);
                if (check && check.data && check.data.connectedClients > 0) {
                    console.log(JSON.stringify({ ...check.data, scriptPath: __filename }, null, 2));
                    return;
                }
            }
        }
        const finalCheck = await httpJsonRequest('GET', '/health', null, 1000).catch(() => ({ data: status }));
        console.log(JSON.stringify({ ...(finalCheck.data || {}), scriptPath: __filename }, null, 2));
        return;
    }

    if (cmd === 'status') {
        try {
            const health = await httpJsonRequest('GET', '/health', null, 1500);
            console.log(JSON.stringify({ ...(health.data || {}), scriptPath: __filename }, null, 2));
        } catch {
            console.log(JSON.stringify({ ok: false, running: false, port: PORT, scriptPath: __filename }, null, 2));
        }
        return;
    }

    if (cmd === 'call') {
        const toolName = args[1];
        if (!toolName) {
            console.error('Использование: node mcp-bridge.js call <tool> [jsonArgs] [--out banner.png]');
            process.exit(1);
        }
        let jsonArgs = '{}';
        let outPath = null;
        for (let i = 2; i < args.length; i++) {
            if (args[i] === '--out' && i + 1 < args.length) {
                outPath = args[++i];
            } else if (!args[i].startsWith('--')) {
                jsonArgs = args[i];
            }
        }

        await ensureDaemonRunning();
        const parsedArgs = JSON.parse(jsonArgs);
        const result = await invokeTool(toolName, parsedArgs);
        const obj = typeof result === 'string' ? JSON.parse(result) : result;

        if (obj && typeof obj.previewPngBase64 === 'string' && obj.previewPngBase64.length > 0) {
            const pngBytes = Buffer.from(obj.previewPngBase64, 'base64');
            const targetFile = outPath
                ? path.resolve(outPath)
                : path.join(require('node:os').tmpdir(), 'banner-mcp-preview.png');
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

main().catch((err) => {
    console.error(err);
    process.exit(1);
});
