package tw.tools.scanner

import android.content.Context
import android.net.ConnectivityManager
import android.net.LinkProperties
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.os.Handler
import android.os.Looper
import android.os.Build
import okhttp3.ConnectionPool
import okhttp3.Dns
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.io.IOException
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.Proxy
import java.net.Socket
import java.net.UnknownHostException
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import javax.net.SocketFactory
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLException
import javax.net.ssl.X509TrustManager

internal data class RegisterState(
    val state: String,
    val epoch: String,
    val register: String,
    val outcome: String,
    val product: String,
    val remainingStock: Double? = null
) {
    val canScan: Boolean get() = state == "scanning" || state == "purchasing" || state == "product_capture"
    constructor(json: JSONObject) : this(
        json.getString("state"), json.getString("epoch"), json.getString("register"),
        if (json.isNull("outcome")) "" else json.optString("outcome"),
        if (json.isNull("product")) "" else json.optString("product"),
        json.optDouble("remainingStock").takeIf { it.isFinite() }
    )
}

internal class ReceiverError(val status: Int) : IOException()
internal class RouteUnavailable : IOException()

internal class Uploader(context: Context) {
    private val networks = context.getSystemService(ConnectivityManager::class.java)
    private val main = Handler(Looper.getMainLooper())
    private val connectionWorker = Executors.newSingleThreadExecutor()
    private val scanWorker = Executors.newSingleThreadExecutor()
    private val http = OkHttpClient.Builder()
        .proxy(Proxy.NO_PROXY)
        .followRedirects(false)
        .followSslRedirects(false)
        .retryOnConnectionFailure(false)
        .connectionPool(ConnectionPool(2, 20, TimeUnit.SECONDS))
        .callTimeout(6, TimeUnit.SECONDS)
        .connectTimeout(2500, TimeUnit.MILLISECONDS)
        .readTimeout(3500, TimeUnit.MILLISECONDS)
        .build()
    private data class Route(val identity: Any, val factory: SocketFactory)
    private data class BoundClient(val address: RegisterAddress, val route: Any, val client: OkHttpClient)
    private var boundClient: BoundClient? = null
    private var routeWatcher: ConnectivityManager.NetworkCallback? = null
    private var routeChanged: Runnable? = null

    fun watchRoutes(onChange: () -> Unit) {
        if (routeWatcher != null) return
        val action = Runnable(onChange)
        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) = schedule()
            override fun onLost(network: Network) = schedule()
            override fun onLinkPropertiesChanged(network: Network, properties: LinkProperties) = schedule()
            private fun schedule() {
                main.removeCallbacks(action)
                main.postDelayed(action, 250)
            }
        }
        val request = NetworkRequest.Builder()
            .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
            .addTransportType(NetworkCapabilities.TRANSPORT_BLUETOOTH)
            .addTransportType(NetworkCapabilities.TRANSPORT_ETHERNET)
            .build()
        runCatching { networks.registerNetworkCallback(request, callback) }.onSuccess {
            routeWatcher = callback
            routeChanged = action
        }
    }

    fun stopWatchingRoutes() {
        routeWatcher?.let { runCatching { networks.unregisterNetworkCallback(it) } }
        routeWatcher = null
        routeChanged?.let(main::removeCallbacks)
        routeChanged = null
    }

    fun pair(link: PairingLink, complete: (Result<Pair<ScannerCredentials, RegisterState>>) -> Unit) {
        run(connectionWorker, complete) {
            val model = (Build.MANUFACTURER + " " + Build.MODEL).trim().take(60)
            val response = request(link.address, "/pair", null, JSONObject().put("code", link.code).put("deviceName", model))
            val refresh = response.optString("refreshToken").takeIf { it.matches(HEX_TOKEN) }
            val credentials = ScannerCredentials(link.address, bearer(response), refresh)
            credentials to RegisterState(response.getJSONObject("scanner"))
        }
    }

    fun ping(credentials: ScannerCredentials, complete: (Result<Pair<ScannerCredentials, RegisterState>>) -> Unit) {
        run(connectionWorker, complete) {
            try {
                credentials to RegisterState(pingOnceMoreOnDisconnect(credentials))
            } catch (error: ReceiverError) {
                val refresh = credentials.refreshToken
                if (error.status != 401 || refresh == null) throw error
                val renewed = request(credentials.address, "/renew", refresh, JSONObject())
                credentials.copy(token = bearer(renewed)) to RegisterState(renewed.getJSONObject("scanner"))
            }
        }
    }

    fun scan(credentials: ScannerCredentials, state: RegisterState, barcode: String, format: String, symbology: String?, gs1: Gs1Barcode?,
             complete: (Result<RegisterState>) -> Unit) {
        val eventId = UUID.randomUUID().toString()
        val payload = JSONObject().put("version", 1).put("eventId", eventId)
            .put("barcode", barcode).put("epoch", state.epoch).put("format", format)
            .put("symbology", symbology)
        if (gs1 != null) payload.put("gs1", JSONObject().put("gtin", gs1.gtin).put("batch", gs1.batch)
            .put("expiryDate", gs1.expiryDate).put("serialNumber", gs1.serialNumber))
        run(scanWorker, complete) {
            val response = request(credentials.address, "/scan", credentials.token, payload)
            if (response.has("eventId") && (response.optString("eventId") != eventId || !response.optBoolean("received")))
                throw IOException("Invalid scan acknowledgment")
            RegisterState(response)
        }
    }

    fun close() {
        stopWatchingRoutes()
        http.dispatcher.cancelAll()
        http.connectionPool.evictAll()
        connectionWorker.shutdownNow()
        scanWorker.shutdownNow()
        main.removeCallbacksAndMessages(null)
    }

    private fun <T> run(worker: java.util.concurrent.ExecutorService, complete: (Result<T>) -> Unit,
                        operation: () -> T) {
        worker.execute {
            val result = runCatching(operation)
            if (!worker.isShutdown) main.post { if (!worker.isShutdown) complete(result) }
        }
    }

    private fun pingOnceMoreOnDisconnect(credentials: ScannerCredentials): JSONObject {
        try {
            return request(credentials.address, "/ping", credentials.token)
        } catch (error: IOException) {
            if (error is ReceiverError || error is RouteUnavailable || error is SSLException) throw error
        }
        // A read-only ping can safely recover a stale connection. Never replay a scan.
        return request(credentials.address, "/ping", credentials.token)
    }

    private fun request(address: RegisterAddress, path: String, token: String?, payload: JSONObject? = null): JSONObject {
        val registerIp = InetAddress.getByAddress(address.host.split('.').map { it.toInt().toByte() }.toByteArray())
        val route = route(address.method, registerIp)
        val target = address.identity ?: address.host
        val client = clientFor(address, route, registerIp, target)
        val request = Request.Builder().url("https://$target:${address.port}$path")
            .header("Accept", "application/json")
        token?.let { request.header("Authorization", "Bearer $it") }
        if (payload != null) request.post(payload.toString().toRequestBody("application/json".toMediaType()))
        try {
            client.newCall(request.build()).execute().use { response ->
                if (!response.isSuccessful) throw ReceiverError(response.code)
                val source = response.body?.source() ?: throw IOException("Empty register response")
                if (source.request(8193)) throw IOException("Register response too large")
                return JSONObject(source.readUtf8())
            }
        } catch (error: IOException) {
            if (error !is ReceiverError) invalidate(client)
            throw error
        }
    }

    @Synchronized
    private fun clientFor(address: RegisterAddress, route: Route, registerIp: InetAddress, target: String): OkHttpClient {
        boundClient?.takeIf { it.address == address && it.route == route.identity }?.let { return it.client }
        http.connectionPool.evictAll()
        val verifier = PinnedCertificate(address.pin)
        val tls = SSLContext.getInstance("TLS").apply { init(null, arrayOf(verifier), null) }
        val client = http.newBuilder()
            .dns(object : Dns {
                override fun lookup(hostname: String): List<InetAddress> =
                    if (hostname == target) listOf(registerIp) else throw UnknownHostException()
            })
            .socketFactory(route.factory)
            .sslSocketFactory(tls.socketFactory, verifier)
            .build()
        boundClient = BoundClient(address, route.identity, client)
        return client
    }

    @Synchronized
    private fun invalidate(client: OkHttpClient) {
        if (boundClient?.client === client) {
            boundClient = null
            http.connectionPool.evictAll()
        }
    }

    private fun bearer(response: JSONObject): String =
        response.getString("token").takeIf { it.matches(HEX_TOKEN) } ?: throw IOException("Invalid session token")

    @Suppress("DEPRECATION") // Find the Wi-Fi network even when it has no internet and Android prefers mobile data.
    private fun route(method: Method, peer: InetAddress): Route {
        for (network in networks.allNetworks) {
            val capabilities = networks.getNetworkCapabilities(network) ?: continue
            val link = networks.getLinkProperties(network) ?: continue
            val matches = when (method) {
                Method.WIFI -> capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) &&
                    capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN)
                Method.BLUETOOTH -> capabilities.hasTransport(NetworkCapabilities.TRANSPORT_BLUETOOTH)
                Method.USB -> matchesInterface(Method.USB, link.interfaceName.orEmpty())
            }
            if (!matches) continue
            val local = link.linkAddresses.firstOrNull {
                sameSubnet(it.address.address, peer.address, it.prefixLength)
            } ?: continue
            return Route(network to local, network.socketFactory)
        }

        // A phone acting as the tethering host may expose only a local interface, with no Android Network.
        for (adapter in NetworkInterface.getNetworkInterfaces()?.toList().orEmpty()) {
            if (!matchesInterface(method, adapter.name) || !adapter.isUp) continue
            val local = adapter.interfaceAddresses.firstOrNull {
                it.address is Inet4Address && sameSubnet(it.address.address, peer.address, it.networkPrefixLength.toInt())
            } ?: continue
            return Route(adapter.name to local, SourceSocketFactory(local.address))
        }
        throw RouteUnavailable()
    }

    private class SourceSocketFactory(private val source: InetAddress) : SocketFactory() {
        override fun createSocket() = Socket().apply { bind(InetSocketAddress(source, 0)) }
        override fun createSocket(host: String, port: Int) = connect(InetSocketAddress(host, port))
        override fun createSocket(host: InetAddress, port: Int) = connect(InetSocketAddress(host, port))
        override fun createSocket(host: String, port: Int, local: InetAddress, localPort: Int) =
            connect(InetSocketAddress(host, port), local, localPort)
        override fun createSocket(host: InetAddress, port: Int, local: InetAddress, localPort: Int) =
            connect(InetSocketAddress(host, port), local, localPort)

        private fun connect(remote: InetSocketAddress, local: InetAddress = source, localPort: Int = 0): Socket {
            check(local == source)
            return Socket().apply { bind(InetSocketAddress(source, localPort)); connect(remote, 2500) }
        }
    }
}

private val HEX_TOKEN = Regex("[0-9A-Fa-f]{64}")

internal fun matchesInterface(method: Method, name: String): Boolean = when (method) {
    Method.BLUETOOTH -> BLUETOOTH_INTERFACE.matches(name)
    Method.USB -> USB_INTERFACE.matches(name)
    Method.WIFI -> false
}

private val USB_INTERFACE = Regex("(rndis|usb|ncm)([0-9]+)?")
private val BLUETOOTH_INTERFACE = Regex("(bt[-_]?pan|bnep)([0-9]+)?")

internal fun sameSubnet(address: ByteArray, peer: ByteArray, prefix: Int): Boolean {
    if (address.size != 4 || peer.size != 4 || prefix !in 1..32 || address.contentEquals(peer)) return false
    return address.indices.all { i ->
        val bits = (prefix - 8 * i).coerceIn(0, 8)
        ((address[i].toInt() xor peer[i].toInt()) and (0xFF shl (8 - bits) and 0xFF)) == 0
    }
}

private class PinnedCertificate(pin: String) : X509TrustManager {
    private val expected = pin.chunked(2).map { it.toInt(16).toByte() }.toByteArray()

    override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
        val leaf = chain?.firstOrNull() ?: throw CertificateException("No register certificate")
        val actual = MessageDigest.getInstance("SHA-256").digest(leaf.encoded)
        if (!MessageDigest.isEqual(expected, actual)) throw CertificateException("Register certificate changed")
        leaf.checkValidity()
    }

    override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {
        throw CertificateException("Client certificates are unsupported")
    }

    override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
}
