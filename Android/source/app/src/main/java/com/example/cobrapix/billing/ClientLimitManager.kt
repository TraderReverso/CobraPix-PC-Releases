package com.example.cobrapix.billing

import com.example.cobrapix.data.ClientDao

sealed class EntitlementResult {
    object Allowed : EntitlementResult()
    object LimitReached : EntitlementResult()
    object SubscriptionCheckUnavailable : EntitlementResult()
}

class ClientLimitManager(
    private val clientDao: ClientDao,
    private val subscriptionRepo: SubscriptionRepository
) {
    suspend fun canCreateOrReactivateClient(): EntitlementResult {
        return when (subscriptionRepo.subscriptionState.value) {
            is SubscriptionState.Loading -> EntitlementResult.SubscriptionCheckUnavailable
            is SubscriptionState.Error -> EntitlementResult.SubscriptionCheckUnavailable
            is SubscriptionState.Pro -> EntitlementResult.Allowed
            is SubscriptionState.Free -> {
                val activeCount = clientDao.getActiveClientCount()
                if (activeCount < 10) {
                    EntitlementResult.Allowed
                } else {
                    EntitlementResult.LimitReached
                }
            }
        }
    }
}
