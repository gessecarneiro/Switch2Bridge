# Histórico

## 0.3.2 — Tela de testes

- Controle redesenhado em vetor, com carcaça em camadas, gatilhos, direcional em cruz e movimento dos analógicos.
- GL e GR separados da vista frontal, com identificação de botões traseiros.
- Estados de repouso, pressionado e já testado com cores distintas no desenho e na lista.
- Nova proporção dos painéis, cartões compactos dos analógicos e legenda visual.
- Desenho estático compartilhado e congelado, preservando a atualização apenas quando as entradas mudam.

## 0.3.1 — Switch2Bridge

- Novo nome no aplicativo, nos executáveis, nos diagnósticos e no código.
- Distribuição dos fontes com licença MIT e avisos de terceiros.
- Compilação e testes no GitHub Actions para Windows x64.
- Compilação gera `bin/Switch2Bridge.exe` e sua configuração do .NET Framework.
- Perfis XML continuam compatíveis com a versão anterior. Para migrar, copie a pasta `profiles` para junto do novo executável, com os aplicativos fechados.

## 0.3.0

- Saída por evento com fila limitada que preserva transições de botões.
- Reutilização de buffers e recursos nativos; planos imutáveis de remapeamento.
- Métricas de latência da leitura HID até o envio ao driver.
- Menos alocações e desenhos da interface; suspensão visual ao minimizar.
- Correção da neutralidade inicial do dispositivo virtual.

## 0.2.1

- Espera limitada e cancelável de prontidão do ViGEmBus para erro Win32 483.
- Diagnóstico identifica a etapa do comando que falhou.

## 0.2.0

- Perfis, remapeamento dos 21 botões e saída Xbox 360 via ViGEmBus.

## 0.1.x

- Janela de testes e leitura inicial do controle por USB.
