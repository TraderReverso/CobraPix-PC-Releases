package com.example.cobrapix.billing

import android.content.Context
import android.app.Activity
import android.os.Handler
import android.os.Looper
import android.widget.Toast
import android.util.Log
import com.android.billingclient.api.*
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

class BillingManager(private val context: Context) {
    private val TAG = "CobraPixBilling"
    private val coroutineScope = CoroutineScope(Dispatchers.IO)

    sealed class PurchasesResult {
        object Loading : PurchasesResult()
        data class Success(val purchases: List<Purchase>) : PurchasesResult()
        data class Error(val message: String) : PurchasesResult()
    }

    private val _purchasesResult = MutableStateFlow<PurchasesResult>(PurchasesResult.Loading)
    val purchasesResult: StateFlow<PurchasesResult> = _purchasesResult.asStateFlow()

    private val _productDetails = MutableStateFlow<List<ProductDetails>>(emptyList())
    val productDetails: StateFlow<List<ProductDetails>> = _productDetails.asStateFlow()

    private val _connectionState = MutableStateFlow(false)
    private var isConnecting = false
    private var connectionAttempt = 0
    private val MAX_CONNECTION_ATTEMPTS = 3

    private val purchasesUpdatedListener = PurchasesUpdatedListener { billingResult, purchasesList ->
        if (billingResult.responseCode == BillingClient.BillingResponseCode.OK && purchasesList != null) {
            Log.d(TAG, "Compras atualizadas: ${purchasesList.size} encontradas.")
            for (purchase in purchasesList) {
                handlePurchase(purchase)
            }
            queryPurchases()
        } else if (billingResult.responseCode == BillingClient.BillingResponseCode.USER_CANCELED) {
            Log.d(TAG, "Usuário cancelou o fluxo de compra.")
        } else {
            Log.e(TAG, "Erro na atualização de compras: ${billingResult.debugMessage}")
            _purchasesResult.value = PurchasesResult.Error(billingResult.debugMessage)
        }
    }

    private var billingClient: BillingClient = BillingClient.newBuilder(context)
        .setListener(purchasesUpdatedListener)
        .enablePendingPurchases(PendingPurchasesParams.newBuilder().enableOneTimeProducts().build())
        .build()

    fun startConnection() {
        if (isConnecting) return
        isConnecting = true
        Log.d(TAG, "Iniciando conexão com BillingClient...")

        billingClient.startConnection(object : BillingClientStateListener {
            override fun onBillingSetupFinished(billingResult: BillingResult) {
                isConnecting = false
                if (billingResult.responseCode == BillingClient.BillingResponseCode.OK) {
                    Log.d(TAG, "BillingClient conectado com sucesso.")
                    connectionAttempt = 0
                    _connectionState.value = true
                    queryProductDetails()
                    queryPurchases()
                } else {
                    Log.e(TAG, "Erro ao conectar BillingClient: ${billingResult.debugMessage}")
                    _connectionState.value = false
                    _purchasesResult.value = PurchasesResult.Error(billingResult.debugMessage)
                }
            }

            override fun onBillingServiceDisconnected() {
                isConnecting = false
                _connectionState.value = false
                
                if (connectionAttempt < MAX_CONNECTION_ATTEMPTS) {
                    connectionAttempt++
                    Log.e(TAG, "BillingClient desconectado. Tentativa de reconexão $connectionAttempt/$MAX_CONNECTION_ATTEMPTS em 3 segundos...")
                    
                    coroutineScope.launch {
                        delay(3000)
                        startConnection()
                    }
                } else {
                    Log.e(TAG, "BillingClient desconectado. Limite máximo de reconexões ($MAX_CONNECTION_ATTEMPTS) atingido. Parando tentativas.")
                    _purchasesResult.value = PurchasesResult.Error("Service Disconnected (Max retries reached)")
                }
            }
        })
    }

    private fun queryProductDetails() {
        coroutineScope.launch {
            if (!_connectionState.value) return@launch
            
            val productList = listOf(
                QueryProductDetailsParams.Product.newBuilder()
                    .setProductId("cobra_pix_pro_monthly")
                    .setProductType(BillingClient.ProductType.SUBS)
                    .build(),
                QueryProductDetailsParams.Product.newBuilder()
                    .setProductId("cobra_pix_pro_yearly")
                    .setProductType(BillingClient.ProductType.SUBS)
                    .build()
            )

            val params = QueryProductDetailsParams.newBuilder()
                .setProductList(productList)
                .build()

            try {
                val result = billingClient.queryProductDetails(params)
                if (result.billingResult.responseCode == BillingClient.BillingResponseCode.OK) {
                    result.productDetailsList?.let {
                        Log.d(TAG, "Detalhes dos produtos recuperados: ${it.size} produtos encontrados.")
                        _productDetails.value = it
                    }
                } else {
                    Log.e(TAG, "Erro ao consultar produtos: ${result.billingResult.debugMessage}")
                }
            } catch (e: Exception) {
                Log.e(TAG, "Erro fatal ao consultar Product Details", e)
            }
        }
    }

    fun queryPurchases() {
        coroutineScope.launch {
            if (!_connectionState.value) {
                Log.e(TAG, "Não é possível consultar compras. BillingClient não está conectado.")
                return@launch
            }

            val params = QueryPurchasesParams.newBuilder()
                .setProductType(BillingClient.ProductType.SUBS)
                .build()

            try {
                val result = billingClient.queryPurchasesAsync(params)
                if (result.billingResult.responseCode == BillingClient.BillingResponseCode.OK) {
                    val activePurchases = result.purchasesList.filter { it.purchaseState == Purchase.PurchaseState.PURCHASED }
                    Log.d(TAG, "Consultas de compras concluídas. ${activePurchases.size} assinaturas PURCHASED ativas.")
                    _purchasesResult.value = PurchasesResult.Success(activePurchases)

                    for (purchase in activePurchases) {
                        if (!purchase.isAcknowledged) {
                            acknowledgePurchase(purchase)
                        }
                    }
                } else {
                    Log.e(TAG, "Erro ao consultar compras (queryPurchasesAsync): ${result.billingResult.debugMessage}")
                    _purchasesResult.value = PurchasesResult.Error(result.billingResult.debugMessage)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Erro fatal ao consultar compras", e)
                _purchasesResult.value = PurchasesResult.Error(e.message ?: "Unknown error")
            }
        }
    }

    fun launchSubscriptionPurchase(activity: Activity, productDetails: ProductDetails, offerToken: String) {
        if (!_connectionState.value) {
            Log.e(TAG, "Falha ao iniciar compra: BillingClient não está conectado.")
            return
        }
        
        if (offerToken.isBlank()) {
            Log.e(TAG, "Falha ao iniciar compra: offerToken inválido ou vazio.")
            return
        }

        val productDetailsParamsList = listOf(
            BillingFlowParams.ProductDetailsParams.newBuilder()
                .setProductDetails(productDetails)
                .setOfferToken(offerToken)
                .build()
        )

        val billingFlowParams = BillingFlowParams.newBuilder()
            .setProductDetailsParamsList(productDetailsParamsList)
            .build()

        val billingResult = billingClient.launchBillingFlow(activity, billingFlowParams)
        if (billingResult.responseCode != BillingClient.BillingResponseCode.OK) {
            Log.e(TAG, "Erro ao iniciar fluxo de compra. Response Code: ${billingResult.responseCode}, Msg: ${billingResult.debugMessage}")
        }
    }

    private fun handlePurchase(purchase: Purchase) {
        if (purchase.purchaseState == Purchase.PurchaseState.PURCHASED) {
            if (!purchase.isAcknowledged) {
                acknowledgePurchase(purchase)
            }
        } else if (purchase.purchaseState == Purchase.PurchaseState.PENDING) {
            Log.d(TAG, "Compra PENDING encontrada. Aguardando processamento do pagamento. Token: ${purchase.purchaseToken.take(10)}...")
            Handler(Looper.getMainLooper()).post {
                Toast.makeText(context, "Pagamento pendente. O PRO será ativado após a confirmação.", Toast.LENGTH_LONG).show()
            }
        }
    }

    private fun acknowledgePurchase(purchase: Purchase) {
        coroutineScope.launch {
            val acknowledgePurchaseParams = AcknowledgePurchaseParams.newBuilder()
                .setPurchaseToken(purchase.purchaseToken)
                .build()

            try {
                val result = billingClient.acknowledgePurchase(acknowledgePurchaseParams)
                if (result.responseCode == BillingClient.BillingResponseCode.OK) {
                    Log.d(TAG, "Compra reconhecida (Acknowledged) com sucesso. Token: ${purchase.purchaseToken.take(10)}...")
                } else {
                    Log.e(TAG, "Erro ao reconhecer compra: ${result.debugMessage}")
                }
            } catch (e: Exception) {
                Log.e(TAG, "Erro fatal ao dar Acknowledge na compra", e)
            }
        }
    }
}
