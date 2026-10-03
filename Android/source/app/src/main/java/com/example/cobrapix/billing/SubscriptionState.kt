package com.example.cobrapix.billing

sealed class SubscriptionState {
    object Loading : SubscriptionState()
    object Free : SubscriptionState()
    data class Pro(val isMonthly: Boolean, val isYearly: Boolean) : SubscriptionState()
    data class Error(val message: String) : SubscriptionState()
}
