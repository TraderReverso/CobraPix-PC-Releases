# Tasks - Sincronização de 7 Horários e Fix de Som (V21)

- [x] Segurança e Sincronização de Dados
    - [x] Atualizar `MainViewModel.kt`: Refatorar `getSafeHours` para 7 slots reais
    - [x] Atualizar `MainViewModel.kt`: Remover horários fixos e usar `settings.getAlarmHours()` dinâmico
- [x] Inteligência de Alarme (Corrente Única)
    - [x] Atualizar `AlarmHelper.kt`: Corrigir cálculo de `currentTimeInMinutes` para precisão total
    - [x] Atualizar `AlarmHelper.kt`: Garantir reuso de IDs determinísticos por parcela
- [x] Interface e UX (Limpeza)
    - [x] Atualizar `AddClientScreen.kt`: Remover lista fixa `listOf(600, ...)` e passar null
    - [x] Atualizar `SettingsScreen.kt`: Revisar fechamento de TimePicker e salvamento seguro
- [x] Som e Notificações (Diagnóstico)
    - [x] Atualizar `NotificationReceiver.kt`: Implementar Canal Dinâmico baseado no hash do Som
    - [x] Adicionar Logs de Diagnóstico (D) para rastrear o carregamento do MP3 e agendamento
- [x] Verificação Técnica
    - [x] Auditoria final: 0 ocorrências de `take(4)` ou `size < 4`
    - [x] Build Final (Assemble Debug): Sucesso
