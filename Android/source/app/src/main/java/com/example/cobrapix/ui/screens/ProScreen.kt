package com.example.cobrapix.ui.screens

import android.app.Activity
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Star
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.example.cobrapix.billing.SubscriptionState
import com.example.cobrapix.ui.MainViewModel
import com.example.cobrapix.ui.components.*
import com.example.cobrapix.ui.theme.LocalCobraPixColors

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ProScreen(
    viewModel: MainViewModel,
    activity: Activity,
    onBack: () -> Unit
) {
    val hudColors = LocalCobraPixColors.current
    val subState by viewModel.subscriptionRepository.subscriptionState.collectAsStateWithLifecycle()
    val products by viewModel.billingManager.productDetails.collectAsStateWithLifecycle()

    Scaffold(
        topBar = {
            TopAppBar(
                title = { SoloHeader("CobraPix PRO", color = hudColors.warning) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, "Voltar", tint = hudColors.textPrimary)
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = Color.Transparent)
            )
        },
        containerColor = Color.Transparent
    ) { padding ->
        Box(modifier = Modifier.fillMaxSize()) {
            SoloBackground()

            Column(
                modifier = Modifier
                    .padding(padding)
                    .fillMaxSize()
                    .padding(16.dp)
                    .verticalScroll(rememberScrollState()),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Icon(Icons.Default.Star, null, tint = hudColors.warning, modifier = Modifier.size(64.dp))
                Spacer(modifier = Modifier.height(16.dp))
                Text(
                    "Desbloqueie o poder máximo.",
                    color = hudColors.textPrimary,
                    fontWeight = FontWeight.Black,
                    fontSize = 18.sp
                )
                Text(
                    "Clientes ilimitados e controle total.",
                    color = hudColors.textSecondary,
                    fontSize = 14.sp
                )
                Spacer(modifier = Modifier.height(32.dp))

                when (val state = subState) {
                    is SubscriptionState.Loading -> {
                        CircularProgressIndicator(color = hudColors.warning)
                        Spacer(Modifier.height(16.dp))
                        Text("Verificando assinatura...", color = hudColors.textSecondary)
                    }
                    is SubscriptionState.Error -> {
                        SoloCard(borderColor = hudColors.error) {
                            Text("Não foi possível verificar sua assinatura.", color = hudColors.error, fontWeight = FontWeight.Bold)
                            Spacer(Modifier.height(8.dp))
                            Text(state.message, color = hudColors.textSecondary, fontSize = 12.sp)
                        }
                    }
                    is SubscriptionState.Pro -> {
                        SoloCard(borderColor = hudColors.success) {
                            Text("COBRAPIX PRO ATIVO", color = hudColors.success, fontWeight = FontWeight.Black, fontSize = 18.sp)
                            Spacer(Modifier.height(8.dp))
                            val planName = when {
                                state.isYearly -> "Plano Anual"
                                state.isMonthly -> "Plano Mensal"
                                else -> "Plano Ativo"
                            }
                            Text("Seu $planName está habilitado. Você já possui clientes ilimitados.", color = hudColors.textPrimary)
                        }
                    }
                    is SubscriptionState.Free -> {
                        val monthlyProduct = products.find { it.productId == "cobra_pix_pro_monthly" }
                        val yearlyProduct = products.find { it.productId == "cobra_pix_pro_yearly" }

                        if (monthlyProduct == null && yearlyProduct == null) {
                            SoloCard(borderColor = hudColors.textSecondary) {
                                Text("Os planos PRO ainda não estão disponíveis para compra.", color = hudColors.textPrimary)
                            }
                        } else {
                            yearlyProduct?.let { product ->
                                // Busca segura de oferta de plano base (tem fases de preço)
                                val bestOffer = product.subscriptionOfferDetails?.find { it.pricingPhases.pricingPhaseList.isNotEmpty() }
                                val offerToken = bestOffer?.offerToken
                                val price = bestOffer?.pricingPhases?.pricingPhaseList?.lastOrNull()?.formattedPrice ?: "Indisponível"

                                SoloCard(borderColor = hudColors.warning) {
                                    Text("PLANO ANUAL", color = hudColors.warning, fontWeight = FontWeight.Black, fontSize = 16.sp)
                                    Text("Economia e tranquilidade o ano todo.", color = hudColors.textSecondary, fontSize = 12.sp)
                                    Spacer(Modifier.height(12.dp))
                                    Text(price, color = hudColors.textPrimary, fontWeight = FontWeight.ExtraBold, fontSize = 24.sp)
                                    Spacer(Modifier.height(16.dp))
                                    
                                    if (!offerToken.isNullOrBlank() && price != "Indisponível") {
                                        Button(
                                            onClick = {
                                                viewModel.billingManager.launchSubscriptionPurchase(activity, product, offerToken)
                                            },
                                            modifier = Modifier.fillMaxWidth(),
                                            colors = ButtonDefaults.buttonColors(containerColor = hudColors.warning)
                                        ) {
                                            Text("ASSINAR ANUAL", fontWeight = FontWeight.Black, color = Color.Black)
                                        }
                                    } else {
                                        Text("Plano temporariamente indisponível.", color = hudColors.error, fontSize = 12.sp)
                                    }
                                }
                                Spacer(Modifier.height(16.dp))
                            }

                            monthlyProduct?.let { product ->
                                val bestOffer = product.subscriptionOfferDetails?.find { it.pricingPhases.pricingPhaseList.isNotEmpty() }
                                val offerToken = bestOffer?.offerToken
                                val price = bestOffer?.pricingPhases?.pricingPhaseList?.lastOrNull()?.formattedPrice ?: "Indisponível"

                                SoloCard(borderColor = hudColors.primary) {
                                    Text("PLANO MENSAL", color = hudColors.primary, fontWeight = FontWeight.Black, fontSize = 16.sp)
                                    Text("Flexibilidade mês a mês.", color = hudColors.textSecondary, fontSize = 12.sp)
                                    Spacer(Modifier.height(12.dp))
                                    Text(price, color = hudColors.textPrimary, fontWeight = FontWeight.ExtraBold, fontSize = 24.sp)
                                    Spacer(Modifier.height(16.dp))
                                    
                                    if (!offerToken.isNullOrBlank() && price != "Indisponível") {
                                        Button(
                                            onClick = {
                                                viewModel.billingManager.launchSubscriptionPurchase(activity, product, offerToken)
                                            },
                                            modifier = Modifier.fillMaxWidth(),
                                            colors = ButtonDefaults.buttonColors(containerColor = hudColors.primary)
                                        ) {
                                            Text("ASSINAR MENSAL", fontWeight = FontWeight.Black, color = Color.White)
                                        }
                                    } else {
                                        Text("Plano temporariamente indisponível.", color = hudColors.error, fontSize = 12.sp)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

