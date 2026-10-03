# Plano de Sincronização e Diagnóstico de Som (V21)

Este plano corrige os resquícios da lógica de 4 horários, garantindo que o ciclo de 7 avisos seja aplicado em todo o sistema, e resolve o problema de falta de som e falhas no TimePicker.

## User Review Required

> [!IMPORTANT]
> **Abandono dos 4 Horários Internos**: Para evitar inconsistências, o motor de cobrança passará a ignorar os campos `hour1-4` do banco de dados e usará SEMPRE os 7 horários globais configurados por você. Isso simplifica o sistema e garante que novas configurações funcionem instantaneamente para todos os clientes.
>
> **Resolução de Som**: O Android "congela" as propriedades do canal de notificação após a criação. Vou implementar uma estratégia de canal dinâmico baseada no caminho do arquivo de som, forçando o sistema a reconhecer o novo MP3 sempre que você mudá-lo.

## Alterações Propostas

### 1. Núcleo de Estabilidade (Null Safety & 7 Slots)

#### [MODIFY] [MainViewModel.kt](file:///C:/Users/Suporte-STC/CobraPix/app/src/main/java/com/example/cobrapix/ui/MainViewModel.kt)
- **`getSafeHours`**: Atualizar para garantir 7 slots (padrão 09h-21h).
- **`addClientWithSale` / `addMonthlyCustomer`**: Remover a passagem de horários fixos. Buscar automaticamente do `SettingsRepository`.
- **`rescheduleAllActiveAlarms`**: Garantir que o reagendamento unificado use a lista de 7 horários.

#### [MODIFY] [AddClientScreen.kt](file:///C:/Users/Suporte-STC/CobraPix/app/src/main/java/com/example/cobrapix/ui/screens/AddClientScreen.kt)
- Remover `listOf(600, 720, 960, 1260)`. Passar `null` para as funções do ViewModel para disparar o uso da configuração global.

### 2. Correção de Som e Notificações

#### [MODIFY] [NotificationReceiver.kt](file:///C:/Users/Suporte-STC/CobraPix/app/src/main/java/com/example/cobrapix/logic/NotificationReceiver.kt)
- **Log de Diagnóstico**: Adicionar logs detalhados para confirmar se o som está sendo carregado.
- **Canal de Som**: Refinar o `channelId` para incluir um sufixo baseado no som selecionado, forçando a atualização do Android.

### 3. Saneamento do AlarmHelper

#### [MODIFY] [AlarmHelper.kt](file:///C:/Users/Suporte-STC/CobraPix/app/src/main/java/com/example/cobrapix/logic/AlarmHelper.kt)
- **`scheduleNextValidAlarm`**: Corrigir cálculo de minutos para evitar duplicidade.
- **`scheduleSingleAlarm`**: Garantir que o tipo `CHAIN` seja processado corretamente pelo Receiver.

## Plano de Verificação

### Testes de Ciclo de 7 Horários
1. Configurar 7 horários personalizados nos Ajustes.
2. Criar um novo cliente.
3. Verificar via logs se o alarme agendado segue a lista personalizada e não o padrão antigo.

### Testes de Som
1. Selecionar um novo MP3.
2. Clicar em "Testar Sinal Sonoro".
3. Verificar se a próxima notificação de cobrança utiliza o som atualizado.

### Verificação Técnica
- Busca global por `take(4)` e `size < 4` (deve ser 0).
- Busca por `600, 720, 960, 1260` fixos (deve ser 0).
