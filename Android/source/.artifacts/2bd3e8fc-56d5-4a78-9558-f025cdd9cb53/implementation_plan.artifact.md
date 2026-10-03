# Plano de Implementação: Geração de QR Code PIX Dinâmico (Fase 3.3)

Este plano detalha a inserção da funcionalidade que permitirá ao usuário gerar um QR Code PIX oficial diretamente pelo aplicativo, facilitando o recebimento por aproximação ou cópia de código.

## 1. Geração do Payload PIX (Copia e Cola)
Para não adicionar bibliotecas desnecessárias que engessem o código, vamos criar uma rotina nativa Kotlin que siga as normas do Banco Central do Brasil para a criação do BR Code (Pix).
- A rotina (`PixGenerator.kt`) montará o bloco `Payload Format Indicator`, o `Merchant Account Information`, etc., formatando os campos (como tamanho, DDI 55) com uso dos dados já existentes em `SettingsRepository` (Chave PIX, Nome do Recebedor, Cidade).
- Incluirá um CRC-16 (Cyclic Redundancy Check) preciso e padronizado em algoritmos de validação polinomial (`x^16 + x^12 + x^5 + 1`) obrigatório para leitura bancária.

## 2. Geração da Imagem do QR Code
Em vez de depender do `Zxing` que exige inchaço do Gradle, podemos usar uma API ou a dependência `com.google.zxing:core` que é o esqueleto limpo. Injetaremos o `core:3.5.3` no `libs.versions.toml`. A conversão pegará a string gerada no passo anterior e a converterá em um `Bitmap` nativo Android.

## 3. UI: Tela e Botão na Parcela Ativa
- No painel da dívida de um cliente (`ClientDetailScreen.kt`), na mesma fileira de PAGO e WHATSAPP (quando houver saldo devedor), aparecerá o botão **"GERAR PIX"**.
- Ao clicar, um Dialog subirá.
  - Ele pré-carregará o valor como sendo o *Saldo Restante* daquela parcela (Valor Total - Pagamento Parcial).
  - Um TextField permitirá que o usuário edite o valor de cobrança, sendo bloqueado em transações `<= 0` ou transações `> Saldo Restante`.
  - O Dialog mostrará a Chave PIX e, logo abaixo, a imagem do QR Code.
  - Botão **"COPIAR PIX"**: Salvará na `ClipboardManager` o Payload string (para Pix Copia e Cola).
  - Botão **"COMPARTILHAR"**: Gerará o PNG no `cacheDir`, injetará via `FileProvider` e abrirá a tela nativa do Android de Share, assim como já feito no Recibo.

## Regras de Segurança
- Nada será escrito no banco de dados. "Gerar PIX" é uma ação isolada. O pagamento final continua exigindo o botão humano "PAGO".
- O botão estará escondido em parcelas já quitadas.
- O botão de Gerar PIX exigirá que a Chave Pix esteja preenchida nos `Ajustes`.

> [!CAUTION]
> Ao lidar com a criação de chaves Pix e código Copia-e-Cola, a precisão do formato EMV é extrema. Espaços não tratados na cidade ou no nome quebrarão a leitura do banco do cliente final.
