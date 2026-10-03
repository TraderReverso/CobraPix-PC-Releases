package com.example.cobrapix.billing

import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

class SubscriptionRepository(private val billingManager: BillingManager) {
    private val TAG = "CobraPixBilling"
    private val coroutineScope = CoroutineScope(Dispatchers.IO)

    private val _subscriptionState = MutableStateFlow<SubscriptionState>(SubscriptionState.Loading)
    val subscriptionState: StateFlow<SubscriptionState> = _subscriptionState.asStateFlow()

    init {
        billingManager.startConnection()
        monitorPurchases()
    }

    private fun monitorPurchases() {
        coroutineScope.launch {
            billingManager.purchasesResult.collect { result ->
                when (result) {
                    is BillingManager.PurchasesResult.Loading -> {
                        Log.d(TAG, "Consultando estado da assinatura...")
                        _subscriptionState.value = SubscriptionState.Loading
                    }
                    is BillingManager.PurchasesResult.Error -> {
                        Log.e(TAG, "Erro na consulta de compras, estado = Error. Mensagem: ${result.message}")
                        _subscriptionState.value = SubscriptionState.Error(result.message)
                    }
                    is BillingManager.PurchasesResult.Success -> {
                        val purchases = result.purchases
                        Log.d(TAG, "Avaliação final concluída: ${purchases.size} compras válidas.")

                        if (purchases.isEmpty()) {
                            _subscriptionState.value = SubscriptionState.Free
                            Log.d(TAG, "Nenhuma compra ativa encontrada. Estado derivado = FREE.")
                            return@collect
                        }

                        var isProMonthly = false
                        var isProYearly = false

                        for (purchase in purchases) {
                            if (purchase.products.contains("cobra_pix_pro_monthly")) {
                                isProMonthly = true
                            }
                            if (purchase.products.contains("cobra_pix_pro_yearly")) {
                                isProYearly = true
                            }
                        }

                        if (isProMonthly || isProYearly) {
                            Log.d(TAG, "Assinatura PRO ativa detectada (Mensal: $isProMonthly, Anual: $isProYearly)")
                            _subscriptionState.value = SubscriptionState.Pro(isMonthly = isProMonthly, isYearly = isProYearly)
                        } else {
                            Log.d(TAG, "Compras encontradas, mas nenhum produto PRO válido/conhecido está ativo. Estado derivado = FREE.")
                            _subscriptionState.value = SubscriptionState.Free
                        }
                    }
                }
            }
        }
    }
}
