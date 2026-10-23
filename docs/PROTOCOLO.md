# Protocolo entre o app e o PC

O programa do PC escuta na porta **8765** (TCP). O app fala com ele por **HTTP** (pareamento, listas, arquivos, tela) e por um **WebSocket** (comandos em tempo real).

## Autenticação

1. O app pede `POST /api/pair` com o código de 6 números mostrado no painel do PC e recebe um **token**.
2. Daí em diante envia o token no cabeçalho `X-Token` (ou na query `?t=`, usada pelo WebSocket e pelos downloads).
3. O PC guarda só o *hash* SHA‑256 do token. Cinco códigos errados bloqueiam o pareamento por 1 minuto.

Rotas abertas (sem token): `GET /api/info`, `GET /app.apk`, `POST /api/pair`, `GET /` e `/manifest.json`.
Rotas em `/api/painel/*` e `GET /painel` só respondem a requisições vindas do próprio PC (`localhost`) com o cabeçalho `X-Painel: 1`.

## Rotas HTTP

| Método e rota | Descrição |
|---|---|
| `GET /api/info` | `{app, id, nome, porta, ips[], mac}` — usado para achar o PC na rede |
| `POST /api/pair` | `{codigo, nome}` → `{token}` |
| `GET /api/sites` · `POST /api/sites` | Lista e grava os botões de sites `{sites:[{titulo,url,cor}]}` |
| `GET /api/cenas` · `POST /api/cenas` | Lista e grava as cenas `{cenas:[{nome,passos[]}]}` |
| `GET /api/agenda` · `POST /api/agenda` | Lista e grava a agenda `{itens:[{titulo,hora,diario,passos[]}]}` |
| `GET /api/pasta?c=<caminho>` | Lista uma pasta (`@` = início com pastas conhecidas e discos) |
| `GET /api/baixar?c=<caminho>&t=<token>` | Baixa um arquivo do PC |
| `POST /api/arquivo?n=<nome>[&abrir=1]` | Envia um arquivo (corpo binário) para a pasta de recebidos |
| `GET /api/tela?w=&q=&m=&t=` | Captura de tela em JPEG (largura, qualidade, monitor) |
| `POST /api/cmd` | Executa um comando de texto (mesma sintaxe do WebSocket), sem resposta |
| `GET /app.apk` | Entrega o APK embutido no programa |

## WebSocket `ws://<ip>:8765/ws?t=<token>`

Cada mensagem é um texto `comando argumentos`. Respostas chegam como `tipo json`.

### Entrada

| Comando | Efeito |
|---|---|
| `m dx dy` | Move o mouse (relativo) |
| `a nx ny` · `am monitor nx ny` | Move o mouse (absoluto, 0–10000 em cada eixo) |
| `b l\|r\|m d\|u\|c` | Botão do mouse: pressionar, soltar ou clicar |
| `w delta` · `z delta` | Roda do mouse · zoom (Ctrl + roda) |
| `k combo` | Tecla ou atalho, ex.: `k ctrl+shift+t`, `k f11`, `k playpause` |
| `t texto` | Digita o texto (Unicode) |

### Sistema

| Comando | Efeito / resposta |
|---|---|
| `vg` · `vs 0..1` | Lê / define o volume → `vol {v, m}` |
| `al` · `as <id>` | Lista / troca a saída de áudio → `aud [{id, n, p}]` |
| `bg` · `bs 0..100` | Lê / define o brilho (notebooks) → `bri {v}` (`-1` = sem suporte) |
| `ag` | "Tocando agora" → `now {titulo, artista, app, estado, pos, fim}` |
| `jl` · `jf h` · `jm h` · `jc h` | Lista janelas → `jan [...]`; trazer para frente, minimizar, fechar |
| `ml` | Lista monitores → `mon [{i, w, h, p}]` |
| `pw lock\|screenoff\|sleep\|restart\|shutdown\|cancel` | Energia |
| `pt minutos` · `pq` | Timer de desligamento (`pt 0` cancela) · restante → `tmr {s}` |
| `cg` · `cs <base64>` | Lê / grava a área de transferência → `clip {t}` |
| `fo <base64 do caminho>` | Abre um arquivo ou pasta no PC |

### Navegador e automação

| Comando | Efeito |
|---|---|
| `o <id do site>` · `u <url>` · `n` | Abre um site, um endereço ou o navegador, em janela e tela cheia |
| `sx <id da cena>` | Executa uma cena |

Os passos de uma cena ou da agenda usam esses mesmos comandos, mais `d <milissegundos>` para esperar.
