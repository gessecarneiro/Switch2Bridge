# Validação

## Switch2Bridge 0.3.2

- Tela de testes redesenhada; não há alteração no protocolo USB, remapeamento ou backend virtual.
- Prévia renderizada em 1264 × 860 e 1044 × 700, com entradas simuladas em repouso e pressionadas. Na janela menor, os cartões inferiores ficam acessíveis pela rolagem vertical.
- 1.307 verificações de lógica e 14 verificações WPF aprovadas, incluindo a preservação do desenho quando não há mudança e a suspensão visual ao minimizar.
- Nenhum novo teste de hardware nesta alteração visual.

## Switch2Bridge 0.3.1

- Compilação x64 a partir dos fontes preparados para o GitHub.
- 1.307 verificações de lógica e 14 verificações WPF aprovadas novamente após a renomeação.
- Interface renderizada com a nova identidade e inspecionada.
- Benchmark atual: 750/750 alterações entregues; p50 0,0496 ms, p95 0,1284 ms e p99 0,1498 ms, com backend falso. Execução local única; não representa latência em jogo.
- Script de comparação com executável legado 0.2.1 executado com sucesso.
- Compilação e testes aprovados no [GitHub Actions, execução inicial](https://github.com/gessecarneiro/Switch2Bridge/actions/runs/39295498371).
- Não foi repetido o teste de hardware na versão 0.3.1; os resultados de hardware abaixo pertencem à base 0.3.0.

## Base 0.3.0

- 1.307 verificações de protocolo, eixos, perfis, equivalência do remapeamento compilado, watchdog, fila limitada, transições rápidas e métricas aprovadas.
- 14 verificações WPF aprovadas, incluindo desenho estático sem redesenhos desnecessários e suspensão da atualização visual quando minimizada.
- Três execuções comparativas do benchmark sintético: a versão nova entregou 750/750 alterações em cada uma, contra 203 a 209/750 na versão 0.2.1. Métodos e números completos em `PERFORMANCE.md`.
- Teste do backend real: 2.000 envios aceitos, buffer/evento reutilizados e estado neutro inicial confirmado por XInput. Dispositivo virtual removido ao terminar cada teste.
- Aplicativo 0.3.0 aberto na sessão normal do Windows com **USB conectado** e **Xbox 360 virtual ativo**. A tela de métricas foi verificada. No momento da inspeção havia apenas uma amostra de inicialização; não há percentis estacionários representativos de movimentação física registrados nesta entrega.
- O benchmark reproduzível `benchmark.ps1` foi executado com sucesso.

Limites: o teste sintético usa backend falso; o lote nativo usa relatórios neutros repetidos. Não se mediu o tempo do botão até o jogo/tela nem redução percentual de CPU. Movimente o controle com a saída ativa para coletar as métricas reais no aplicativo.

## Histórico da versão 0.2.1

- 283 verificações automatizadas aprovadas, incluindo atraso de inicialização do driver (483), repetição limitada da espera, erros não transitórios e cancelamento.
- Teste nativo na sessão normal do Windows: protocolo aceito, Xbox virtual criado, dispositivo pronto, relatório neutro aceito e dispositivo removido ao fim do diagnóstico.
- XInput reconheceu um controle no slot 0 durante o teste nativo.
- Aplicativo 0.2.1 aberto com USB conectado e **Xbox 360 virtual ativo**; o indicador foi confirmado na interface e no log.
- O teste em jogo ainda está pendente. O teste de XInput verificou presença do dispositivo; não simulou botões nem confirmou cada ação em um jogo.

## Histórico da versão 0.2.0

- Compilação x64 concluída com o compilador do .NET Framework, sem pacotes externos.
- 279 verificações automatizadas aprovadas: decodificação 0x05/0x09, 21 botões, normalização, zona morta, remapeamento, combinações, perfis/XML, validação, ciclo de saída e liberação por falta de dados.
- 11 verificações WPF em processo separado aprovadas: navegação, 21 linhas de remapeamento, seleção de saída, gravação do GL, nome do perfil, cópia, zona morta, tela de saída, erro por falta de driver, demonstração e bloqueio de saída simulada.
- Páginas de perfis e saída renderizadas e inspecionadas.
- Aplicativo 0.2.0 executado na sessão normal do Windows. Fluxo físico USB `0x09`, 64 bytes, aproximadamente 250 relatórios/s. Valores brutos dos dois analógicos observados.

Naquele momento o driver ainda não estava instalado. Os testes automatizados de ciclo de saída usaram um backend falso. Na versão 0.2.1, a criação do dispositivo real foi verificada conforme os itens acima. Bluetooth, vibração, giroscópio, ocultação do controle físico e troca automática de perfil por jogo não estão implementados.
