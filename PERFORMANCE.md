# Desempenho — base 0.3.0

Resultados coletados antes da renomeação de Switch2 Lab para Switch2Bridge. A versão 0.3.1 mantém o mesmo caminho de entrada e saída.

Comparação local com a versão 0.2.1, em 19/10/2026. Os resultados abaixo separam o teste sintético do envio ao driver real. Eles **não medem latência de jogo nem do botão até a tela**.

## Entrega de alterações a 250 Hz — backend de teste

O benchmark agenda 750 mudanças em três segundos, alternando um botão e um identificador de eixo. Mede do início de `XboxOutput.Update` até o backend falso receber cada alteração. Executa em processo separado, sem controles físicos, sem ViGEmBus e sem alterar a resolução global dos timers. O produtor usa espera ativa apenas no benchmark para gerar os intervalos; o aplicativo não usa espera ativa.

| Versão / execução | Alterações entregues | p50 (ms) | p95 (ms) | p99 (ms) | Máximo (ms) |
|---|---:|---:|---:|---:|---:|
| 0.2.1 / 1 | 209/750 | 2,2493 | 3,8461 | 3,9914 | 8,1237 |
| 0.2.1 / 2 | 203/750 | 1,9105 | 3,7549 | 3,9780 | 5,1358 |
| 0.2.1 / 3 | 208/750 | 1,9536 | 3,8933 | 3,9801 | 15,1629 |
| 0.3.0 / 1 | 750/750 | 0,0625 | 0,1351 | 0,1709 | 1,1649 |
| 0.3.0 / 2 | 750/750 | 0,1132 | 0,1348 | 0,1621 | 0,8357 |
| 0.3.0 / 3 | 750/750 | 0,1147 | 0,1333 | 0,1505 | 0,9211 |

Mediana dos p50 das três execuções: **1,954 → 0,113 ms**, aproximadamente 94% menor. A versão anterior substituía estados intermediários enquanto a thread aguardava seu intervalo. A nova sinaliza a chegada de alterações e conserva as transições de botões/gatilhos numa fila limitada.

Os percentis da versão antiga incluem apenas alterações efetivamente entregues; as descartadas não entram no cálculo. Portanto, a taxa de entrega deve ser considerada junto com os tempos. O benchmark não garante que todos os jogos observem um pressionamento muito curto, pois cada jogo tem seu próprio ciclo de leitura.

## Alocação na decodificação e remapeamento

50.000 iterações, após aquecimento, com parser público que preserva a propriedade do relatório. A versão nova usa um `MappingPlan` imutável, como no caminho real de entrada. A contagem é a alocação gerenciada total do domínio do processo dividida pelo número de iterações, não o tamanho da memória retida.

| Medida | 0.2.1 | 0.3.0 |
|---|---:|---:|
| Bytes gerenciados por relatório | 545,76 | 144,51 |
| Tempo das três execuções (ms) | 35,414 / 24,652 / 28,189 | 16,285 / 14,905 / 12,915 |

Redução aproximada de **74% na alocação** desta etapa. Os tempos são execuções curtas e sujeitos a JIT, coleta de lixo e carga do Windows; não são um estudo estatístico de desempenho.

## Envio ao ViGEmBus real

Teste separado na sessão normal do Windows, com 100 envios de aquecimento e lotes de 2.000 relatórios neutros. O teste chama as duas versões por reflexão com os mesmos argumentos pré-criados; os números incluem esse custo comum. Não são envios espaçados a 250 Hz nem leitura de controle físico.

| Medida | 0.2.1 | 0.3.0 |
|---|---:|---:|
| Tempo do lote, execução 1 | 18,184 ms | 6,575 ms |
| Tempo do lote, execução 2 | 14,755 ms | 6,173 ms |
| Alocação gerenciada por chamada | 867,79 bytes | 32,77 bytes |

Os dois backends criaram e removeram o dispositivo e concluíram os 2.000 comandos. A versão nova reutiliza buffer nativo, OVERLAPPED e evento, sem criá-los por envio. O teste também encontrou e corrigiu uma particularidade da inicialização do ViGEmBus: seu pacote de boot contém eixos não neutros, embora seu cache comece zerado. A versão nova inicializa com uma unidade mínima de eixo e imediatamente envia zero, antes de publicar o estado ativo. O estado neutro foi confirmado via XInput na versão 0.3.0.

O driver ignora relatórios repetidos que já coincidem com seu cache. Assim, esse lote mede principalmente o custo da chamada e do tratamento no driver, não 2.000 alterações consumidas por um jogo. Relatórios diferentes podem enfrentar espera pelo próximo pedido USB IN do XUSB; essa espera agora tem repetição limitada e é incluída nas métricas ao vivo.

## Métricas reais no aplicativo

Na aba **Saída Xbox**, com o backend ativo, mova os analógicos ou pressione botões. O painel mostra mediana, p95, p99, máximo e contagem numa janela de até 4.096 alterações aceitas pelo driver. A tela atualiza os números uma vez por segundo.

A origem do tempo é a conclusão da leitura HID, antes da cópia/decodificação. O final é a conclusão bem-sucedida do envio ao ViGEmBus. Leituras que não alteram a saída não geram amostras, e envios de manutenção/neutralização não são apresentados como entrada física. Valores de inicialização e carga do sistema podem aparecer nos percentis. A medição não inclui transporte anterior à leitura HID, aquisição pelo jogo, renderização ou tela.

## Proteções e limites

- Fila fixa de 256 estados; movimentos de eixos consecutivos podem ser agrupados, preservando transições de botões/gatilhos.
- Ao exceder a fila, a saída é neutralizada e retomada do estado atual; o incidente aparece no contador.
- Estados antigos não são reproduzidos indefinidamente. Após 250 ms sem entrada física, o backend retorna ao neutro, sujeito ao agendamento do Windows.
- Perfis são editados na thread da interface e publicados como planos imutáveis. A gravação dos arquivos ocorre fora do bloqueio de entrada. A gravação ainda é síncrona na interface, mas não bloqueia a leitura/remapeamento.
- Desenhos são atualizados apenas quando necessário, com cores/geometria em cache. A interface minimizada deixa de desenhar e verifica manutenção uma vez por segundo. O envio continua na thread própria.
- Não foram alterados prioridade global do processo, configuração de energia, taxa do USB ou timers globais. Não há redução percentual de CPU medida nesta entrega.

## Reproduzir

`./benchmark.ps1 -AssemblyPath .\bin\Switch2Bridge.exe -OutputDirectory .\work\bench-new`

Para comparar com um executável 0.2.1 preservado: use o mesmo comando com seu caminho, outra pasta de saída e `-Legacy`. O script compila e executa `tests/PerformanceBench.cs`. O teste usa backend falso e não aciona o controle virtual. Os resultados variam com a carga e o agendamento do Windows.
