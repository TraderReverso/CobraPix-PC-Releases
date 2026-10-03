package com.example.cobrapix

import android.os.Bundle
import android.util.Log
import androidx.activity.compose.setContent
import androidx.fragment.app.FragmentActivity
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.background
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.People
import androidx.compose.material.icons.filled.PriorityHigh
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Sync
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.navigation.NavType
import androidx.navigation.compose.*
import androidx.navigation.navArgument
import androidx.navigation.navDeepLink
import com.example.cobrapix.ui.MainViewModel
import com.example.cobrapix.ui.screens.*
import com.example.cobrapix.ui.theme.*

import androidx.biometric.BiometricManager
import androidx.biometric.BiometricPrompt
import androidx.core.content.ContextCompat

class MainActivity : FragmentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        installSplashScreen()
        super.onCreate(savedInstanceState)
        setContent {
            val viewModel: MainViewModel = viewModel()
            val themeMode by viewModel.settings.themeMode.collectAsStateWithLifecycle(initialValue = "DARK")
            val isBiometricEnabled by viewModel.settings.isBiometricEnabled.collectAsStateWithLifecycle(initialValue = null as Boolean?)
            var isAuthenticated by remember { mutableStateOf(false) }
            
            LaunchedEffect(isBiometricEnabled) {
                if (isBiometricEnabled == true && !isAuthenticated) {
                    Log.d("CobraPixBiometric", "Configuração lida: biometric_enabled=true")
                    val biometricManager = BiometricManager.from(this@MainActivity)
                    val canAuth = biometricManager.canAuthenticate(BiometricManager.Authenticators.BIOMETRIC_STRONG or BiometricManager.Authenticators.DEVICE_CREDENTIAL)
                    Log.d("CobraPixBiometric", "BiometricManager.canAuthenticate() = $canAuth")
                    
                    if (canAuth == BiometricManager.BIOMETRIC_SUCCESS) {
                        val executor = ContextCompat.getMainExecutor(this@MainActivity)
                        val biometricPrompt = BiometricPrompt(this@MainActivity, executor,
                            object : BiometricPrompt.AuthenticationCallback() {
                                override fun onAuthenticationError(errorCode: Int, errString: CharSequence) {
                                    super.onAuthenticationError(errorCode, errString)
                                    Log.e("CobraPixBiometric", "Erro/Cancelado: $errString")
                                }

                                override fun onAuthenticationSucceeded(result: BiometricPrompt.AuthenticationResult) {
                                    super.onAuthenticationSucceeded(result)
                                    Log.d("CobraPixBiometric", "Autenticação bem-sucedida")
                                    isAuthenticated = true
                                }

                                override fun onAuthenticationFailed() {
                                    super.onAuthenticationFailed()
                                    Log.e("CobraPixBiometric", "Falha na autenticação (não reconhecido)")
                                }
                            })

                        val promptInfo = BiometricPrompt.PromptInfo.Builder()
                            .setTitle("Desbloquear CobraPix")
                            .setSubtitle("Confirme sua identidade")
                            .setAllowedAuthenticators(BiometricManager.Authenticators.BIOMETRIC_STRONG or BiometricManager.Authenticators.DEVICE_CREDENTIAL)
                            .build()

                        Log.d("CobraPixBiometric", "BiometricPrompt iniciado automaticamente")
                        biometricPrompt.authenticate(promptInfo)
                    } else {
                        Log.e("CobraPixBiometric", "Biometria indisponível no aparelho, mantendo bloqueado ou exigindo configuração")
                    }
                } else if (isBiometricEnabled == false) {
                    Log.d("CobraPixBiometric", "Configuração lida: biometric_enabled=false. Acesso liberado.")
                    isAuthenticated = true
                }
            }

            HUDAnimationProvider {
                CobraPixTheme(darkTheme = themeMode == "DARK") {
                    if (isBiometricEnabled == null) {
                        // Tela de carregamento enquanto lê o DataStore
                        Box(modifier = Modifier.fillMaxSize().background(Color.Black))
                    } else if (isBiometricEnabled == true && !isAuthenticated) {
                        // Lock screen overlay
                        val hudColors = LocalCobraPixColors.current
                        Box(modifier = Modifier.fillMaxSize().background(hudColors.background), contentAlignment = androidx.compose.ui.Alignment.Center) {
                            Column(horizontalAlignment = androidx.compose.ui.Alignment.CenterHorizontally) {
                                Text("COBRAPIX BLOQUEADO", color = hudColors.error, fontWeight = FontWeight.Black, letterSpacing = 2.sp)
                                Spacer(modifier = Modifier.height(16.dp))
                                Button(
                                    onClick = {
                                        val executor = ContextCompat.getMainExecutor(this@MainActivity)
                                        val biometricPrompt = BiometricPrompt(this@MainActivity, executor,
                                            object : BiometricPrompt.AuthenticationCallback() {
                                                override fun onAuthenticationError(errorCode: Int, errString: CharSequence) {
                                                    super.onAuthenticationError(errorCode, errString)
                                                    Log.e("CobraPixBiometric", "Erro/Cancelado: $errString")
                                                }
                                                override fun onAuthenticationSucceeded(result: BiometricPrompt.AuthenticationResult) {
                                                    super.onAuthenticationSucceeded(result)
                                                    Log.d("CobraPixBiometric", "Autenticação bem-sucedida pelo botão")
                                                    isAuthenticated = true
                                                }
                                                override fun onAuthenticationFailed() {
                                                    super.onAuthenticationFailed()
                                                    Log.e("CobraPixBiometric", "Falha na autenticação")
                                                }
                                            })
                                        val promptInfo = BiometricPrompt.PromptInfo.Builder()
                                            .setTitle("Desbloquear CobraPix")
                                            .setAllowedAuthenticators(BiometricManager.Authenticators.BIOMETRIC_STRONG or BiometricManager.Authenticators.DEVICE_CREDENTIAL)
                                            .build()
                                        Log.d("CobraPixBiometric", "BiometricPrompt iniciado manualmente")
                                        biometricPrompt.authenticate(promptInfo)
                                    },
                                    colors = ButtonDefaults.buttonColors(containerColor = hudColors.primary)
                                ) {
                                    Text("DESBLOQUEAR")
                                }
                            }
                        }
                    } else {
                        val navController = rememberNavController()
                    var showAddClientDialog by remember { mutableStateOf(false) }
                    val hudColors = LocalCobraPixColors.current

                    Scaffold(
                        bottomBar = {
                            val navBackStackEntry by navController.currentBackStackEntryAsState()
                            val currentDestination = navBackStackEntry?.destination
                            
                            NavigationBar(
                                containerColor = hudColors.background.copy(alpha = 0.95f),
                                tonalElevation = 12.dp
                            ) {
                                NavigationBarItem(
                                    icon = { Icon(Icons.Default.Home, contentDescription = "Início") },
                                    label = { Text("INÍCIO", fontSize = 9.sp, fontWeight = FontWeight.Black) },
                                    selected = currentDestination?.route == "dashboard",
                                    onClick = { navController.navigate("dashboard") },
                                    colors = NavigationBarItemDefaults.colors(
                                        selectedIconColor = hudColors.primary,
                                        selectedTextColor = hudColors.primary,
                                        unselectedIconColor = hudColors.textSecondary.copy(alpha = 0.5f),
                                        unselectedTextColor = hudColors.textSecondary.copy(alpha = 0.5f),
                                        indicatorColor = hudColors.primary.copy(alpha = 0.15f)
                                    )
                                )
                                NavigationBarItem(
                                    icon = { Icon(Icons.Default.People, contentDescription = "Clientes") },
                                    label = { Text("CLIENTES", fontSize = 9.sp, fontWeight = FontWeight.Black) },
                                    selected = currentDestination?.route == "clients",
                                    onClick = { navController.navigate("clients") },
                                    colors = NavigationBarItemDefaults.colors(
                                        selectedIconColor = hudColors.secondary,
                                        selectedTextColor = hudColors.secondary,
                                        unselectedIconColor = hudColors.textSecondary.copy(alpha = 0.5f),
                                        unselectedTextColor = hudColors.textSecondary.copy(alpha = 0.5f),
                                        indicatorColor = hudColors.secondary.copy(alpha = 0.15f)
                                    )
                                )
                                NavigationBarItem(
                                    icon = { Icon(Icons.Default.PriorityHigh, contentDescription = "Atrasados") },
                                    label = { Text("ATRASADOS", fontSize = 9.sp, fontWeight = FontWeight.Black) },
                                    selected = currentDestination?.route == "overdue",
                                    onClick = { navController.navigate("overdue") },
                                    colors = NavigationBarItemDefaults.colors(
                                        selectedIconColor = hudColors.error,
                                        selectedTextColor = hudColors.error,
                                        unselectedIconColor = hudColors.textSecondary.copy(alpha = 0.5f),
                                        unselectedTextColor = hudColors.textSecondary.copy(alpha = 0.5f),
                                        indicatorColor = hudColors.error.copy(alpha = 0.15f)
                                    )
                                )
                                NavigationBarItem(
                                    icon = { Icon(Icons.Default.Sync, contentDescription = "Mensal") },
                                    label = { Text("MENSAL", fontSize = 9.sp, fontWeight = FontWeight.Black) },
                                    selected = currentDestination?.route == "mensalistas",
                                    onClick = { navController.navigate("mensalistas") },
                                    colors = NavigationBarItemDefaults.colors(
                                        selectedIconColor = hudColors.primaryVariant,
                                        selectedTextColor = hudColors.primaryVariant,
                                        unselectedIconColor = hudColors.textSecondary.copy(alpha = 0.5f),
                                        unselectedTextColor = hudColors.textSecondary.copy(alpha = 0.5f),
                                        indicatorColor = hudColors.primaryVariant.copy(alpha = 0.15f)
                                    )
                                )
                                NavigationBarItem(
                                    icon = { Icon(Icons.Default.Settings, contentDescription = "Ajustes") },
                                    label = { Text("AJUSTES", fontSize = 9.sp, fontWeight = FontWeight.Black) },
                                    selected = currentDestination?.route == "settings",
                                    onClick = { navController.navigate("settings") },
                                    colors = NavigationBarItemDefaults.colors(
                                        selectedIconColor = hudColors.textPrimary,
                                        selectedTextColor = hudColors.textPrimary,
                                        unselectedIconColor = hudColors.textSecondary.copy(alpha = 0.5f),
                                        unselectedTextColor = hudColors.textSecondary.copy(alpha = 0.5f),
                                        indicatorColor = hudColors.textPrimary.copy(alpha = 0.15f)
                                    )
                                )
                            }
                        },
                        containerColor = hudColors.background
                    ) { innerPadding ->
                        NavHost(
                            navController = navController,
                            startDestination = "dashboard",
                            modifier = Modifier.padding(innerPadding)
                        ) {
                            composable("dashboard") {
                                DashboardScreen(
                                    viewModel = viewModel,
                                    onAddClient = { showAddClientDialog = true },
                                    onClientClick = { id -> navController.navigate("clientDetail/$id") },
                                    onReportsClick = { navController.navigate("reports") }
                                )
                            }
                            composable("reports") {
                                ReportsScreen(
                                    viewModel = viewModel,
                                    onBack = { navController.popBackStack() },
                                    onNavigateToCashFlow = { navController.navigate("cashFlow") }
                                )
                            }
                            composable("cashFlow") {
                                CashFlowScreen(
                                    viewModel = viewModel,
                                    onBack = { navController.popBackStack() }
                                )
                            }
                            composable("clients") {
                                ClientsScreen(
                                    viewModel = viewModel,
                                    onClientClick = { id -> navController.navigate("clientDetail/$id") }
                                )
                            }
                            composable("overdue") {
                                OverdueScreen(
                                    viewModel = viewModel,
                                    onClientClick = { id -> navController.navigate("clientDetail/$id") }
                                )
                            }
                            composable("mensalistas") {
                                MensalistasScreen(
                                    viewModel = viewModel,
                                    onClientClick = { id -> navController.navigate("clientDetail/$id") }
                                )
                            }
                            composable("settings") {
                                SettingsScreen(
                                    viewModel = viewModel,
                                    onBack = { navController.popBackStack() },
                                    onNavigateToPro = { navController.navigate("pro") }
                                )
                            }
                            composable("pro") {
                                ProScreen(
                                    viewModel = viewModel,
                                    activity = this@MainActivity,
                                    onBack = { navController.popBackStack() }
                                )
                            }
                            composable(
                                "clientDetail/{clientId}",
                                arguments = listOf(navArgument("clientId") { type = NavType.LongType }),
                                deepLinks = listOf(navDeepLink { uriPattern = "cobrapix://client/{clientId}" })
                            ) { backStackEntry ->
                                val clientId = backStackEntry.arguments?.getLong("clientId") ?: -1L
                                if (clientId == -1L) {
                                    LaunchedEffect(Unit) { navController.popBackStack() }
                                } else {
                                    ClientDetailScreen(
                                        viewModel = viewModel,
                                        clientId = clientId,
                                        onBack = { navController.popBackStack() },
                                        onEditClient = { navController.navigate("editClient/$clientId") },
                                        onEditInstallment = { id -> navController.navigate("editInstallment/$id") },
                                        onEditMonthlyContract = { id -> navController.navigate("editMonthlyContract/$id") }
                                    )
                                }
                            }
                            composable(
                                "editMonthlyContract/{clientId}",
                                arguments = listOf(navArgument("clientId") { type = NavType.LongType })
                            ) { backStackEntry ->
                                val clientId = backStackEntry.arguments?.getLong("clientId") ?: -1L
                                if (clientId == -1L) {
                                    LaunchedEffect(Unit) { navController.popBackStack() }
                                } else {
                                    EditMonthlyContractScreen(
                                        viewModel = viewModel,
                                        clientId = clientId,
                                        onBack = { navController.popBackStack() }
                                    )
                                }
                            }
                            composable(
                                "editClient/{clientId}",
                                arguments = listOf(navArgument("clientId") { type = NavType.LongType })
                            ) { backStackEntry ->
                                val clientId = backStackEntry.arguments?.getLong("clientId") ?: -1L
                                if (clientId == -1L) {
                                    LaunchedEffect(Unit) { navController.popBackStack() }
                                } else {
                                    EditClientScreen(
                                        viewModel = viewModel,
                                        clientId = clientId,
                                        onBack = { navController.popBackStack() }
                                    )
                                }
                            }
                            composable(
                                "editInstallment/{id}",
                                arguments = listOf(navArgument("id") { type = NavType.LongType })
                            ) { backStackEntry ->
                                val id = backStackEntry.arguments?.getLong("id") ?: -1L
                                if (id == -1L) {
                                    LaunchedEffect(Unit) { navController.popBackStack() }
                                } else {
                                    EditInstallmentScreen(
                                        viewModel = viewModel,
                                        installmentId = id,
                                        onBack = { navController.popBackStack() }
                                    )
                                }
                            }
                        }
                    }

                    if (showAddClientDialog) {
                        Dialog(onDismissRequest = { showAddClientDialog = false }) {
                            AddClientScreen(
                                viewModel = viewModel,
                                onBack = { showAddClientDialog = false }
                            )
                        }
                    }
                } // Fechamento do Scaffold
                } // Fechamento do else (isAuthenticated)
            } // Fechamento do Theme
        } // Fechamento do HUDAnimationProvider
    } // Fechamento do setContent
} // Fechamento da classe

