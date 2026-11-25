package tw.tools.scanner

import android.Manifest
import android.app.LocaleManager
import android.app.NotificationManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.res.ColorStateList
import android.graphics.PointF
import android.media.AudioManager
import android.media.ToneGenerator
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.LocaleList
import android.os.SystemClock
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.provider.Settings
import android.text.InputType
import android.util.Size
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.View
import android.view.WindowManager
import android.view.inputmethod.EditorInfo
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.TextView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.appcompat.app.AppCompatDelegate
import androidx.camera.core.Camera
import androidx.camera.core.CameraSelector
import androidx.camera.core.FocusMeteringAction
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.core.UseCaseGroup
import androidx.camera.core.resolutionselector.ResolutionSelector
import androidx.camera.core.resolutionselector.ResolutionStrategy
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.core.content.ContextCompat
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import androidx.core.os.LocaleListCompat
import androidx.core.text.BidiFormatter
import androidx.core.text.TextDirectionHeuristicsCompat
import androidx.core.view.doOnLayout
import com.google.android.material.snackbar.Snackbar
import tw.tools.scanner.databinding.ActivityMainBinding
import tw.tools.scanner.databinding.DialogSettingsBinding
import java.io.InterruptedIOException
import java.net.ConnectException
import java.net.NoRouteToHostException
import java.text.DateFormat
import java.util.Date
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference
import javax.net.ssl.SSLException

class MainActivity : AppCompatActivity() {
    private lateinit var b: ActivityMainBinding
    private lateinit var prefs: Prefs
    private lateinit var uploader: Uploader
    private val cameraExecutor = Executors.newSingleThreadExecutor()
    private val decoder by lazy(LazyThreadSafetyMode.NONE) { BarcodeDecoder() }
    private val scanGate = CameraScanGate()
    private data class FrameObservation(val barcode: CameraBarcode?, val time: Long)
    private val observedFrame = AtomicReference<FrameObservation?>()
    private val main = Handler(Looper.getMainLooper())
    private var settingsView: DialogSettingsBinding? = null
    private var manualPairOpen = false
    private var camera: Camera? = null
    private var tone: ToneGenerator? = null
    private var toneVolume = -1
    private var connected = false
    private var lastWarning = -1000L
    private var continuous = true
    private var armed = true
    private var torchOn = false
    private var notice: Snackbar? = null
    private var lastNotice = ""
    private var lastNoticeTime = 0L
    private var connectionInterrupted = false
    private var sentCount = 0
    private var foreground = false
    private var pairing = false
    private var pendingPairing: PairingLink? = null
    private var pinging = false
    private var scanning = false
    private var stateVersion = 0L
    private var register: RegisterState? = null

    private val heartbeat = object : Runnable {
        override fun run() {
            checkServer()
            main.postDelayed(this, if (connected) 1800 else 1200)
        }
    }

    private val requestCamera = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) startCamera() else b.hint.setText(R.string.no_camera_permission)
    }

    override fun attachBaseContext(base: Context) {
        prefs = Prefs(base)
        val savedLanguage = if (prefs.language == "ur") "ur-PK" else "en"
        if (Build.VERSION.SDK_INT >= 33) {
            val manager = base.getSystemService(LocaleManager::class.java)
            val current = manager.applicationLocales[0]?.language
            if (current == "ur" || current == "en") prefs.language = current
            else manager.applicationLocales = LocaleList.forLanguageTags(savedLanguage)
        } else {
            AppCompatDelegate.setApplicationLocales(LocaleListCompat.forLanguageTags(savedLanguage))
        }
        super.attachBaseContext(base)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        b = ActivityMainBinding.inflate(layoutInflater)
        setContentView(b.root)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        volumeControlStream = AudioManager.STREAM_MUSIC
        uploader = Uploader(this)
        wireUi()
        cameraExecutor.execute { decoder }
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED)
            startCamera()
        else if (savedInstanceState == null) requestCamera.launch(Manifest.permission.CAMERA)
        else b.hint.setText(R.string.no_camera_permission)
    }

    override fun onResume() {
        super.onResume()
        foreground = true
        uploader.watchRoutes(::checkServer)
        main.post(heartbeat)
    }

    override fun onPause() {
        foreground = false
        register = null
        scanGate.reset()
        observedFrame.set(null)
        notice?.dismiss()
        uploader.stopWatchingRoutes()
        if (!pairing) stateVersion++
        main.removeCallbacks(heartbeat)
        super.onPause()
    }

    override fun onDestroy() {
        uploader.close()
        cameraExecutor.shutdown()
        tone?.release()
        super.onDestroy()
    }

    private fun wireUi() {
        updateCount()
        b.preview.previewStreamState.observe(this) { state ->
            if (state == PreviewView.StreamState.STREAMING) focusForScan()
        }
        var tapPoint: PointF? = null
        b.preview.setOnClickListener {
            val point = tapPoint
            tapPoint = null
            if (point == null) focusForScan() else focusForScan(point.x, point.y)
        }
        b.preview.setOnTouchListener { view, event ->
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN -> true
                MotionEvent.ACTION_UP -> {
                    tapPoint = PointF(event.x, event.y)
                    view.performClick()
                    true
                }
                else -> false
            }
        }
        b.settingsBtn.setOnClickListener { showSettings() }
        b.retryBtn.setOnClickListener {
            scanGate.reset()
            pendingPairing?.let(::pair) ?: checkServer()
        }
        b.modeBtn.setOnClickListener {
            continuous = !continuous
            armed = continuous
            scanGate.reset()
            b.modeBtn.setText(if (continuous) R.string.continuous else R.string.single)
            b.modeBtn.isSelected = !continuous
            updateScanHint()
        }
        b.torchBtn.setOnClickListener {
            val activeCamera = camera ?: return@setOnClickListener
            if (!activeCamera.cameraInfo.hasFlashUnit()) {
                toast(R.string.no_flash)
                return@setOnClickListener
            }
            torchOn = !torchOn
            activeCamera.cameraControl.enableTorch(torchOn)
            b.torchBtn.isSelected = torchOn
        }
        b.clearBtn.setOnClickListener {
            b.list.removeAllViews()
            sentCount = 0
            updateCount()
        }
        b.manualBtn.setOnClickListener { sendManual() }
        b.manualInput.setOnEditorActionListener { _, actionId, _ ->
            if (actionId != EditorInfo.IME_ACTION_SEND) return@setOnEditorActionListener false
            sendManual()
            true
        }
    }

    private fun sendManual() {
        val code = b.manualInput.text.toString().trim()
        if (code.isEmpty()) return
        if (accept(code, "manual")) b.manualInput.setText("")
        else if (register?.canScan != true || !prefs.enabled) showNotice(getString(R.string.scan_retry))
    }

    private fun showSettings() {
        val view = DialogSettingsBinding.inflate(layoutInflater)
        settingsView = view
        val popup = MaterialAlertDialogBuilder(this).setTitle(R.string.settings)
            .setView(view.root).setPositiveButton(R.string.done, null).create()
        view.languageList.adapter = choices(listOf(getString(R.string.language_en), getString(R.string.language_ur)))
        view.languageList.setSelection(if (prefs.language == "ur") 1 else 0)
        view.methodList.adapter = choices(Method.values().map { getString(it.title) })
        view.methodList.setSelection(prefs.method.ordinal)
        view.soundCheck.isChecked = prefs.sound
        view.vibrateCheck.isChecked = prefs.vibrate
        view.successCheck.isChecked = prefs.successSounds
        view.connectionCheck.isChecked = prefs.connectionSounds
        view.warningCheck.isChecked = prefs.warningSounds
        view.volume.progress = prefs.volume
        view.soundCheck.setOnCheckedChangeListener { _, selected -> prefs.sound = selected }
        view.vibrateCheck.setOnCheckedChangeListener { _, selected -> prefs.vibrate = selected }
        view.successCheck.setOnCheckedChangeListener { _, selected -> prefs.successSounds = selected }
        view.connectionCheck.setOnCheckedChangeListener { _, selected -> prefs.connectionSounds = selected }
        view.warningCheck.setOnCheckedChangeListener { _, selected -> prefs.warningSounds = selected }
        view.volume.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(bar: SeekBar?, value: Int, fromUser: Boolean) {
                if (fromUser) prefs.volume = value
            }
            override fun onStartTrackingTouch(bar: SeekBar?) = Unit
            override fun onStopTrackingTouch(bar: SeekBar?) = Unit
        })
        view.testSoundBtn.setOnClickListener { play(Cue.SUCCESS, test = true) }
        view.languageList.onItemSelectedListener = onSelection { position ->
            val language = if (position == 1) "ur" else "en"
            if (language != prefs.language) {
                prefs.language = language
                popup.dismiss()
                AppCompatDelegate.setApplicationLocales(LocaleListCompat.forLanguageTags(if (language == "ur") "ur-PK" else "en"))
            }
        }
        view.methodList.onItemSelectedListener = onSelection { position -> changeMethod(Method.values()[position]) }
        view.bluetoothBtn.setOnClickListener { openSystemSettings(Settings.ACTION_BLUETOOTH_SETTINGS) }
        view.tetherBtn.setOnClickListener { openSystemSettings(Settings.ACTION_WIRELESS_SETTINGS) }
        view.connectBtn.setOnClickListener {
            if (prefs.enabled && prefs.connection != null) {
                prefs.enabled = false
                clearConnectionState()
                setStatus(false, getString(R.string.disconnected))
            } else {
                prefs.enabled = true
                clearConnectionState()
                if (prefs.connection == null) popup.dismiss()
                checkServer()
            }
        }
        view.manualPairBtn.setOnClickListener {
            popup.dismiss()
            showManualPairing()
        }
        view.forgetBtn.setOnClickListener {
            MaterialAlertDialogBuilder(this).setTitle(R.string.forget).setMessage(R.string.unpair_note)
                .setNegativeButton(R.string.cancel, null)
                .setPositiveButton(R.string.forget) { _, _ ->
                    prefs.forget(prefs.method)
                    prefs.enabled = true
                    clearConnectionState()
                    showPairPrompt()
                }.show().styleActions(stopping = true)
        }
        popup.setOnDismissListener { settingsView = null }
        refreshSettings()
        popup.show()
        popup.styleActions()
    }

    private fun onSelection(action: (Int) -> Unit) = object : AdapterView.OnItemSelectedListener {
        override fun onItemSelected(parent: AdapterView<*>?, view: View?, position: Int, id: Long) = action(position)
        override fun onNothingSelected(parent: AdapterView<*>?) = Unit
    }

    private fun choices(items: List<String>) =
        ArrayAdapter(this, R.layout.settings_choice, items).apply {
            setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        }

    private fun changeMethod(method: Method) {
        if (prefs.method != method) {
            prefs.method = method
            clearConnectionState()
            setStatus(false, getString(if (prefs.enabled && prefs.connection != null) R.string.reconnecting else R.string.disconnected))
            checkServer()
        }
        refreshSettings()
    }

    private fun clearConnectionState() {
        stateVersion++
        register = null
        pendingPairing = null
        scanGate.reset()
        connectionInterrupted = false
        b.retryBtn.visibility = View.GONE
        connectionChanged(false)
    }

    private fun refreshSettings() {
        val view = settingsView ?: return
        val method = prefs.method
        val connection = prefs.connection
        view.guideText.setText(method.guide)
        view.bluetoothBtn.visibility = if (method == Method.BLUETOOTH) View.VISIBLE else View.GONE
        view.tetherBtn.visibility = if (method == Method.WIFI) View.GONE else View.VISIBLE
        view.deviceText.text = register?.register ?: connection?.address?.let { ltr("${it.host}:${it.port}") }
            ?: getString(R.string.no_device)
        view.connectionText.text = b.status.text
        val canDisconnect = prefs.enabled && connection != null
        view.connectBtn.setText(if (canDisconnect) R.string.disconnect else R.string.connect)
        view.connectBtn.backgroundTintList = ContextCompat.getColorStateList(this,
            if (canDisconnect) R.color.stop_button else R.color.primary_button)
        view.methodList.isEnabled = !scanning && !pairing
        view.languageList.isEnabled = !scanning && !pairing
        view.connectBtn.isEnabled = !scanning && !pairing
        view.forgetBtn.isEnabled = !scanning && !pairing
        view.manualPairBtn.isEnabled = !scanning && !pairing
        view.forgetBtn.visibility = if (connection == null) View.GONE else View.VISIBLE
    }

    private fun openSystemSettings(action: String) {
        runCatching { startActivity(Intent(action)) }.onFailure { toast(R.string.os_settings_unavailable) }
    }

    private fun showManualPairing() {
        manualPairOpen = true
        val layout = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(22), dp(12), dp(22), dp(8))
        }
        layout.addView(TextView(this).apply { setText(R.string.manual_help) })

        fun entry(label: Int, input: Int): EditText = EditText(this).apply {
            setHint(label)
            inputType = input or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
            maxLines = 1
            textDirection = View.TEXT_DIRECTION_LTR
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO
            layout.addView(this)
        }

        val link = entry(R.string.pair_link, InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI)
        val ip = entry(R.string.ip_address, InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI)
        val port = entry(R.string.port, InputType.TYPE_CLASS_NUMBER).apply { setText(8765.toString()) }
        val pin = entry(R.string.fingerprint, InputType.TYPE_CLASS_TEXT)
        val code = entry(R.string.code, InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD)
        val dialog = MaterialAlertDialogBuilder(this).setTitle(R.string.manual_pair)
            .setView(ScrollView(this).apply { addView(layout) }).setNegativeButton(R.string.cancel, null)
            .setPositiveButton(R.string.connect, null).create()
        dialog.setOnDismissListener { manualPairOpen = false }
        dialog.setOnShowListener {
            dialog.styleActions()
            dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener {
                val pairingLink = if (link.text.isNotBlank()) PairingLink.parse(link.text.toString())
                else PairingLink.fromFields(ip.text.toString(), port.text.toString(), pin.text.toString(),
                    code.text.toString(), prefs.method, REGISTER_IDENTITY)
                when {
                    pairingLink == null -> toast(R.string.invalid_pairing)
                    pairingLink.address.method != prefs.method -> wrongMethod(pairingLink.address.method)
                    else -> {
                        dialog.dismiss()
                        pair(pairingLink)
                    }
                }
            }
        }
        dialog.show()
    }

    private fun AlertDialog.styleActions(stopping: Boolean = false) {
        getButton(AlertDialog.BUTTON_POSITIVE)?.apply {
            backgroundTintList = null
            setBackgroundResource(R.drawable.bg_dialog_action)
            if (stopping) backgroundTintList = ContextCompat.getColorStateList(this@MainActivity, R.color.stop_button)
            setTextColor(color(R.color.action_white))
            setPadding(dp(16), 0, dp(16), 0)
            minHeight = dp(48)
            textSize = 16f
            isAllCaps = false
        }
        getButton(AlertDialog.BUTTON_NEGATIVE)?.apply {
            setTextColor(color(R.color.action_white))
            textSize = 16f
            isAllCaps = false
        }
    }

    private fun wrongMethod(method: Method) {
        Toast.makeText(this, getString(R.string.wrong_method, getString(method.title)), Toast.LENGTH_LONG).show()
    }

    private fun pair(link: PairingLink) {
        val method = link.address.method
        if (!foreground || pairing || scanning) return
        if (method != prefs.method) {
            wrongMethod(method)
            return
        }
        pairing = true
        pendingPairing = link
        val version = ++stateVersion
        b.retryBtn.visibility = View.GONE
        setStatus(false, getString(R.string.connecting))
        uploader.pair(link) complete@{ result ->
            pairing = false
            if (method != prefs.method || version != stateVersion) return@complete
            result.onSuccess { (credentials, state) ->
                pendingPairing = null
                if (runCatching { prefs.remember(credentials) }.isSuccess) {
                    prefs.enabled = true
                    if (foreground) applyState(state)
                    b.code.setText(R.string.paired_prompt)
                } else setStatus(false, getString(R.string.pair_saved_failed))
            }.onFailure { error ->
                val message = when {
                    error is ReceiverError && error.status == 401 -> getString(R.string.pair_code_error)
                    error is SSLException -> getString(R.string.register_identity_error)
                    error is RouteUnavailable || error is NoRouteToHostException -> getString(routeMessage())
                    error is InterruptedIOException || error is ConnectException ->
                        unreachableMessage(link.address)
                    else -> getString(R.string.pair_failed)
                }
                when (error) {
                    is RouteUnavailable, is NoRouteToHostException, is InterruptedIOException, is ConnectException ->
                        b.retryBtn.visibility = View.VISIBLE
                    else -> pendingPairing = null
                }
                if (foreground) {
                    play(Cue.WARNING)
                    showNotice(message)
                }
                setStatus(false, message)
            }
        }
    }

    private fun checkServer() {
        if (!foreground || pairing || pinging || scanning || !prefs.enabled) return
        val credentials = prefs.connection ?: run {
            if (b.status.text.isBlank() || b.status.text == getString(R.string.disconnected)) showPairPrompt()
            return
        }
        val version = stateVersion
        pinging = true
        uploader.ping(credentials) { result ->
            pinging = false
            if (foreground && prefs.enabled && prefs.connection == credentials && version == stateVersion) {
                result.onSuccess { (active, state) ->
                    if (active == credentials || runCatching { prefs.remember(active) }.isSuccess) {
                        applyState(state)
                    } else {
                        register = null
                        connectionChanged(false)
                        setStatus(false, getString(R.string.pair_saved_failed))
                    }
                }.onFailure { connectionFailed(it) }
            }
        }
    }

    private fun showPairPrompt() {
        b.retryBtn.visibility = View.GONE
        setStatus(false, getString(R.string.scan_pair_hint))
    }

    private fun connectionFailed(error: Throwable) {
        val hadConnection = connected
        register = null
        connectionChanged(false)
        if ((error as? ReceiverError)?.status == 401) {
            prefs.forget(prefs.method)
            clearConnectionState()
            setStatus(false, getString(R.string.pair_pc_again))
            showNotice(getString(R.string.pair_pc_again))
        } else {
            b.retryBtn.visibility = View.VISIBLE
            val message = when (error) {
                is SSLException -> getString(R.string.register_identity_error)
                is RouteUnavailable, is NoRouteToHostException -> getString(routeMessage())
                is InterruptedIOException, is ConnectException -> prefs.connection?.address?.let(::unreachableMessage)
                    ?: getString(R.string.reconnecting)
                else -> getString(R.string.reconnecting)
            }
            setStatus(false, message)
            if (hadConnection || error is SSLException) showNotice(message)
            connectionInterrupted = connectionInterrupted || hadConnection
        }
    }

    private fun routeMessage() = when (prefs.method) {
        Method.BLUETOOTH -> R.string.bluetooth_route_unavailable
        Method.WIFI -> R.string.wifi_route_unavailable
        Method.USB -> R.string.route_unavailable
    }

    private fun unreachableMessage(address: RegisterAddress): String {
        val endpoint = ltr("${address.host}:${address.port}")
        val text = if (address.method == Method.WIFI) R.string.wifi_register_unreachable
            else R.string.register_unreachable
        return getString(text, endpoint)
    }

    private fun applyState(state: RegisterState) {
        register = state
        connectionChanged(true)
        if (connectionInterrupted) {
            connectionInterrupted = false
            showNotice(getString(R.string.connection_restored))
        }
        b.retryBtn.visibility = View.GONE
        val label = when (state.state) {
            "scanning" -> R.string.ready
            "purchasing" -> R.string.ready_purchase
            "product_capture" -> R.string.ready_capture
            "receiving" -> R.string.pause_receiving
            "unknown" -> R.string.pause_unknown
            "product_entry" -> R.string.pause_product
            "checkout" -> R.string.pause_checkout
            "away" -> R.string.pause_away
            else -> R.string.pause_idle
        }
        setStatus(state.canScan, getString(R.string.register_status,
            BidiFormatter.getInstance().unicodeWrap(state.register), getString(label)))
    }

    private fun setStatus(ready: Boolean, text: String) {
        if (b.status.text != text) b.status.text = text
        b.dot.backgroundTintList = ColorStateList.valueOf(color(if (ready) R.color.ok else R.color.muted))
        refreshSettings()
    }

    private fun startCamera() {
        val future = ProcessCameraProvider.getInstance(this)
        future.addListener({
            if (isDestroyed) return@addListener
            b.preview.doOnLayout {
                if (isDestroyed) return@doOnLayout
                runCatching {
                    val provider = future.get()
                    val preview = Preview.Builder().build().also { it.setSurfaceProvider(b.preview.surfaceProvider) }
                    val resolution = ResolutionSelector.Builder().setResolutionStrategy(
                        ResolutionStrategy(Size(1280, 960), ResolutionStrategy.FALLBACK_RULE_CLOSEST_LOWER_THEN_HIGHER)
                    ).build()
                    val analysis = ImageAnalysis.Builder()
                        .setTargetRotation(b.preview.display.rotation)
                        .setResolutionSelector(resolution)
                        .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                        .build().apply { setAnalyzer(cameraExecutor, ::analyze) }
                    val group = UseCaseGroup.Builder().addUseCase(preview).addUseCase(analysis)
                    b.preview.viewPort?.let { group.setViewPort(it) }
                    provider.unbindAll()
                    camera = provider.bindToLifecycle(this, CameraSelector.DEFAULT_BACK_CAMERA, group.build())
                }.onFailure { b.hint.setText(R.string.camera_error) }
            }
        }, ContextCompat.getMainExecutor(this))
    }

    private fun focusForScan(x: Float = b.preview.width / 2f, y: Float = b.preview.height / 2f) {
        val active = camera ?: return
        val point = b.preview.meteringPointFactory.createPoint(x, y, 0.22f)
        val action = FocusMeteringAction.Builder(point, FocusMeteringAction.FLAG_AF or FocusMeteringAction.FLAG_AE)
            .setAutoCancelDuration(1500, TimeUnit.MILLISECONDS)
            .build()
        if (active.cameraInfo.isFocusMeteringSupported(action)) active.cameraControl.startFocusAndMetering(action)
    }

    private fun analyze(frame: ImageProxy) {
        val now = SystemClock.elapsedRealtime()
        try {
            val decoded = decoder.decode(frame, now) ?: return
            val result = FrameObservation(decoded.barcode, now)
            if (observedFrame.getAndSet(result) == null) {
                main.post { observedFrame.getAndSet(null)?.let(::cameraResult) }
            }
        } finally {
            frame.close()
        }
    }

    private fun cameraResult(frame: FrameObservation) {
        if (!foreground || isDestroyed || settingsView != null || manualPairOpen) return
        val barcode = frame.barcode
        if (!scanGate.observe(barcode, frame.time) || barcode == null) return
        if (barcode.text.startsWith("techpos://pair")) {
            if (!pairing && !scanning && (prefs.connection == null || !prefs.enabled)) {
                PairingLink.parse(barcode.text)?.let {
                    scanGate.submitted(barcode)
                    pair(it)
                }
            }
            return
        }
        if (barcode.text.length > 512 || barcode.text.any { it.isISOControl() && it != '\u001d' }) {
            scanGate.submitted(barcode)
            showScanResult(R.string.invalid_barcode, getString(R.string.bad_code_retry), true)
            play(Cue.WARNING)
        } else if (armed && accept(barcode.text, barcode.format, barcode.symbology)) {
            scanGate.submitted(barcode)
        }
    }

    private fun accept(code: String, format: String, symbology: String? = null): Boolean {
        if (!foreground || !prefs.enabled || pairing || scanning) return false
        val credentials = prefs.connection ?: return false
        val state = register?.takeIf { it.canScan } ?: return false
        val marked = Gs1Barcode.marked(code, symbology)
        val gs1 = if (marked) Gs1Barcode.parse(code) else null
        if (code.length > 512 || marked && gs1 == null || (code.startsWith("https://", true) || code.startsWith("http://", true)) && !marked || code.any { it.isISOControl() && (!marked || it != '\u001d') }) {
            showScanResult(R.string.invalid_barcode, getString(R.string.bad_code_retry), true)
            play(Cue.WARNING)
            return false
        }
        if (!continuous && format != "manual") {
            armed = false
            updateScanHint()
        }
        scanning = true
        val version = ++stateVersion
        b.eyebrow.setText(R.string.reading)
        b.code.text = ltr(code)
        b.pending.setText(R.string.sending)
        b.pending.visibility = View.VISIBLE
        uploader.scan(credentials, state, code, format, symbology, gs1) { result ->
            b.pending.visibility = View.GONE
            scanning = false
            if (foreground && prefs.enabled && prefs.connection == credentials && version == stateVersion) {
                result.onSuccess { response ->
                    applyState(response)
                    when (response.outcome) {
                        "added" -> {
                            showScanResult(R.string.added, response.product.ifBlank { ltr(code) })
                            recordAccepted(code, response.product)
                            play(Cue.SUCCESS)
                            if (response.remainingStock == 0.0) showNotice(getString(R.string.last_stock, response.product))
                        }
                        "purchase_added" -> {
                            showScanResult(R.string.purchase_added, response.product.ifBlank { ltr(code) })
                            recordAccepted(code, response.product)
                            play(Cue.SUCCESS)
                        }
                        "captured" -> {
                            showScanResult(R.string.barcode_captured, ltr(code))
                            play(Cue.SUCCESS)
                        }
                        "duplicate" -> {
                            showScanResult(R.string.barcode_duplicate, response.product, true)
                            play(Cue.WARNING)
                        }
                        "unknown" -> {
                            showScanResult(R.string.not_found, getString(R.string.not_found_detail, ltr(code)), true)
                            play(Cue.WARNING)
                        }
                        "inactive", "stock" -> {
                            showScanResult(if (response.outcome == "inactive") R.string.inactive else R.string.stock,
                                response.product, true)
                            play(Cue.WARNING)
                        }
                        "paused" -> showScanResult(R.string.scan_paused, getString(R.string.scan_retry), true)
                        "pending" -> showScanResult(R.string.medicine_pending, response.product)
                        "location_selected" -> { showScanResult(R.string.location_selected, response.product); play(Cue.SUCCESS) }
                        "busy" -> showScanResult(R.string.busy, getString(R.string.scan_retry), true)
                        "batch_required", "batch_unknown", "batch_discrepancy", "expired", "held", "serial_duplicate" -> {
                            showScanResult(R.string.check_batch_on_pos, response.product, true)
                            play(Cue.WARNING)
                        }
                        else -> {
                            showScanResult(R.string.lookup_failed, getString(R.string.check_register), true)
                            play(Cue.WARNING)
                        }
                    }
                }.onFailure { error ->
                    if (error is ReceiverError && error.status == 400) {
                        showScanResult(R.string.rejected, getString(R.string.bad_code_retry), true)
                        play(Cue.WARNING)
                    } else {
                        if (error is ReceiverError && error.status == 401 && credentials.refreshToken != null) {
                            register = null
                            checkServer()
                        } else connectionFailed(error)
                        showScanResult(R.string.delivery_unconfirmed, getString(R.string.check_cart), true)
                    }
                }
            }
        }
        return true
    }

    private fun showScanResult(label: Int, message: CharSequence, warn: Boolean = false) {
        b.eyebrow.setText(label)
        b.code.text = message
        if (warn) showNotice(listOf(getString(label), message).filter { it.isNotBlank() }.joinToString("\n"))
    }

    private fun showNotice(message: String) {
        val now = SystemClock.elapsedRealtime()
        if (message == lastNotice && now - lastNoticeTime < 4000) return
        lastNotice = message
        lastNoticeTime = now
        notice?.dismiss()
        notice = Snackbar.make(b.root, message, Snackbar.LENGTH_LONG)
            .setAnchorView(b.manualInput)
            .setBackgroundTint(color(R.color.panel))
            .setTextColor(color(R.color.action_white))
            .setTextMaxLines(4)
            .also { it.show() }
    }

    private fun recordAccepted(code: String, product: String) {
        sentCount++
        updateCount()
        addRow(ltr(code), product)
    }

    private fun updateCount() { b.countLabel.text = getString(R.string.count, sentCount) }

    private fun addRow(code: String, product: String) {
        val time = DateFormat.getTimeInstance(DateFormat.MEDIUM).format(Date())
        val row = TextView(this).apply {
            text = listOf(code, BidiFormatter.getInstance().unicodeWrap(product), ltr(time)).joinToString("  ·  ")
            setTextColor(color(R.color.text_main))
            textSize = 15f
            setPadding(dp(16), dp(9), dp(16), dp(9))
        }
        b.list.addView(row, 0)
        while (b.list.childCount > 60) b.list.removeViewAt(b.list.childCount - 1)
        b.listScroll.scrollTo(0, 0)
    }

    private fun ltr(value: String) = BidiFormatter.getInstance().unicodeWrap(value, TextDirectionHeuristicsCompat.LTR)
    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()
    private fun color(id: Int) = ContextCompat.getColor(this, id)
    private fun toast(message: Int) = Toast.makeText(this, message, Toast.LENGTH_SHORT).show()

    private enum class Cue { SUCCESS, WARNING, CONNECTED, DISCONNECTED }

    private fun connectionChanged(value: Boolean) {
        if (connected == value) return
        connected = value
        play(if (value) Cue.CONNECTED else Cue.DISCONNECTED)
    }

    private fun play(cue: Cue, test: Boolean = false) {
        val audio = getSystemService(AudioManager::class.java)
        if (cue == Cue.SUCCESS && prefs.vibrate && !test && audio.ringerMode != AudioManager.RINGER_MODE_SILENT) vibrate()
        val quiet = getSystemService(NotificationManager::class.java).currentInterruptionFilter ==
            NotificationManager.INTERRUPTION_FILTER_NONE
        if (quiet || prefs.volume == 0 || audio.getStreamVolume(AudioManager.STREAM_MUSIC) == 0) {
            if (test) toast(R.string.media_muted)
            return
        }
        val allowed = when (cue) {
            Cue.SUCCESS -> prefs.successSounds
            Cue.WARNING -> prefs.warningSounds
            Cue.CONNECTED, Cue.DISCONNECTED -> prefs.connectionSounds
        }
        if (!test && (!prefs.sound || !allowed)) return
        if (cue == Cue.WARNING) {
            val now = SystemClock.elapsedRealtime()
            if (now - lastWarning < 400) return
            lastWarning = now
        }
        if (tone == null || toneVolume != prefs.volume) {
            tone?.release()
            toneVolume = prefs.volume
            tone = runCatching { ToneGenerator(AudioManager.STREAM_MUSIC, toneVolume) }.getOrNull()
        }
        val value = when (cue) {
            Cue.SUCCESS -> ToneGenerator.TONE_PROP_BEEP
            Cue.WARNING -> ToneGenerator.TONE_PROP_NACK
            Cue.CONNECTED -> ToneGenerator.TONE_PROP_ACK
            Cue.DISCONNECTED -> ToneGenerator.TONE_PROP_PROMPT
        }
        tone?.startTone(value, if (cue == Cue.SUCCESS) 120 else 140)
    }

    @Suppress("DEPRECATION")
    private fun vibrate() {
        val vibrator = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S)
            getSystemService(VibratorManager::class.java).defaultVibrator
        else getSystemService(Vibrator::class.java)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O)
            vibrator.vibrate(VibrationEffect.createOneShot(35, VibrationEffect.DEFAULT_AMPLITUDE))
        else vibrator.vibrate(35)
    }

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        val trigger = event.keyCode == KeyEvent.KEYCODE_VOLUME_DOWN || event.keyCode == KeyEvent.KEYCODE_VOLUME_UP
        if (!trigger || settingsView != null || manualPairOpen || b.manualInput.hasFocus())
            return super.dispatchKeyEvent(event)
        if (event.action == KeyEvent.ACTION_DOWN && event.repeatCount == 0) {
            armed = true
            scanGate.reset()
            if (!continuous) focusForScan()
            updateScanHint()
        }
        return true
    }

    private fun updateScanHint() {
        b.hint.textSize = if (continuous) 16f else 19f
        b.hint.setText(when {
            continuous -> R.string.volume_hint
            armed -> R.string.point_barcode
            else -> R.string.single_hint
        })
    }
}
