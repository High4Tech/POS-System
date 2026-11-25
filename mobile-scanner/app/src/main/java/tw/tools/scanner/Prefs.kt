package tw.tools.scanner

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONObject
import java.net.URI
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

internal enum class Method(val id: String, val title: Int, val guide: Int) {
    WIFI("wifi", R.string.wifi, R.string.wifi_guide),
    BLUETOOTH("bluetooth", R.string.bluetooth, R.string.bluetooth_guide),
    USB("usb", R.string.usb, R.string.usb_guide);

    companion object {
        fun fromId(id: String?) = values().firstOrNull { it.id == id }
    }
}

internal data class RegisterAddress(val host: String, val port: Int, val pin: String, val method: Method,
                                    val identity: String? = null)

internal data class PairingLink(val address: RegisterAddress, val code: String) {
    companion object {
        private val hex64 = Regex("[0-9A-Fa-f]{64}")
        private val hex48 = Regex("[0-9A-Fa-f]{48}")

        fun parse(value: String): PairingLink? {
            val uri = runCatching { URI(value.trim()) }.getOrNull() ?: return null
            if (uri.scheme != "techpos" || uri.rawAuthority != "pair" ||
                !uri.rawPath.isNullOrEmpty() || uri.rawFragment != null) return null
            val entries = uri.rawQuery?.split('&')?.map { it.split('=', limit = 2) } ?: return null
            if (entries.any { it.size != 2 } || entries.map { it[0] }.toSet().size != entries.size) return null
            val query = entries.associate { it[0] to it[1] }
            if (query["v"] != "1") return null
            val method = query["method"]?.let { Method.fromId(it) ?: return null } ?: Method.WIFI
            val identity = query["identity"]
            if (identity != null && identity != REGISTER_IDENTITY) return null
            return fromFields(query["host"].orEmpty(), query["port"].orEmpty(),
                query["pin"].orEmpty(), query["code"].orEmpty(), method, identity)
        }

        fun fromFields(host: String, port: String, pin: String, code: String, method: Method,
                       identity: String? = null): PairingLink? {
            val address = host.trim()
            val parts = address.split('.').mapNotNull { it.toIntOrNull() }
            if (parts.size != 4 || parts.any { it !in 0..255 } || parts.joinToString(".") != address) return null
            val local = parts[0] == 10 || parts[0] == 172 && parts[1] in 16..31 ||
                parts[0] == 192 && parts[1] == 168 || parts[0] == 169 && parts[1] == 254
            val number = port.trim().toIntOrNull() ?: return null
            val fingerprint = pin.trim().uppercase(java.util.Locale.ROOT)
            val secret = code.trim().uppercase(java.util.Locale.ROOT)
            if (!local || number !in 1..65535 || !hex64.matches(fingerprint) || !hex48.matches(secret)) return null
            return PairingLink(RegisterAddress(address, number, fingerprint, method, identity), secret)
        }
    }
}

internal const val REGISTER_IDENTITY = "pos-scannar.local"
internal data class ScannerCredentials(val address: RegisterAddress, val token: String, val refreshToken: String? = null)

internal class Prefs(context: Context) {
    private val store = context.getSharedPreferences("scanner", Context.MODE_PRIVATE)
    private val cached = mutableMapOf<Method, ScannerCredentials?>()

    init {
        if (store.contains("registerToken")) {
            check(store.edit().remove("registerToken").remove("registerHost")
                .remove("registerPort").remove("registerPin").commit())
        }
    }

    var method: Method
        get() = Method.fromId(store.getString("method", null)) ?: Method.WIFI
        set(value) { store.edit().putString("method", value.id).apply() }

    var language: String
        get() = if (store.getString("language", "en") == "ur") "ur" else "en"
        set(value) { store.edit().putString("language", value).apply() }

    var sound: Boolean
        get() = store.getBoolean("sound", true)
        set(value) { store.edit().putBoolean("sound", value).apply() }

    var vibrate: Boolean
        get() = store.getBoolean("vibrate", true)
        set(value) { store.edit().putBoolean("vibrate", value).apply() }

    var successSounds: Boolean
        get() = store.getBoolean("success_sounds", true)
        set(value) { store.edit().putBoolean("success_sounds", value).apply() }

    var connectionSounds: Boolean
        get() = store.getBoolean("connection_sounds", true)
        set(value) { store.edit().putBoolean("connection_sounds", value).apply() }

    var warningSounds: Boolean
        get() = store.getBoolean("warning_sounds", true)
        set(value) { store.edit().putBoolean("warning_sounds", value).apply() }

    var volume: Int
        get() = store.getInt("sound_volume", 90).coerceIn(0, 100)
        set(value) { store.edit().putInt("sound_volume", value.coerceIn(0, 100)).apply() }

    var enabled: Boolean
        get() = store.getBoolean("enabled-${method.id}", true)
        set(value) { store.edit().putBoolean("enabled-${method.id}", value).apply() }

    val connection: ScannerCredentials? get() = credentials(method)

    fun credentials(method: Method): ScannerCredentials? {
        if (cached.containsKey(method)) return cached[method]
        val saved = store.getString("pairing-${method.id}", null)
        val value = saved?.let {
            runCatching {
                val bytes = Base64.decode(it, Base64.NO_WRAP)
                val cipher = Cipher.getInstance(TRANSFORMATION)
                cipher.init(Cipher.DECRYPT_MODE, getKey(), GCMParameterSpec(128, bytes.copyOfRange(0, 12)))
                cipher.updateAAD(method.id.toByteArray(Charsets.UTF_8))
                val data = JSONObject(String(cipher.doFinal(bytes, 12, bytes.size - 12), Charsets.UTF_8))
                ScannerCredentials(RegisterAddress(data.getString("host"), data.getInt("port"),
                    data.getString("pin"), method, data.optString("identity").takeIf { it == REGISTER_IDENTITY }),
                    data.getString("token"), data.optString("refreshToken").takeIf { it.isNotBlank() && it != "null" })
            }.getOrNull()
        }
        cached[method] = value
        return value
    }

    fun remember(value: ScannerCredentials) {
        val address = value.address
        val data = JSONObject().put("host", address.host).put("port", address.port)
            .put("pin", address.pin).put("token", value.token)
            .put("identity", address.identity).put("refreshToken", value.refreshToken)
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, getKey())
        check(cipher.iv.size == 12)
        cipher.updateAAD(address.method.id.toByteArray(Charsets.UTF_8))
        val bytes = cipher.iv + cipher.doFinal(data.toString().toByteArray(Charsets.UTF_8))
        check(store.edit().putString("pairing-${address.method.id}", Base64.encodeToString(bytes, Base64.NO_WRAP)).commit())
        cached[address.method] = value
    }

    fun forget(method: Method) {
        store.edit().remove("pairing-${method.id}").apply()
        cached[method] = null
    }

    private fun getKey(): SecretKey {
        val keys = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (keys.getKey(KEY, null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder(KEY, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build())
        }.generateKey()
    }

    companion object {
        private const val TRANSFORMATION = "AES/GCM/NoPadding"
        private const val KEY = "tech-store-scanner-pairing-v1"
    }
}
