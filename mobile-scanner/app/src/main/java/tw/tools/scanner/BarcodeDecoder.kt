package tw.tools.scanner

import android.graphics.Bitmap
import android.graphics.Rect
import androidx.camera.core.ImageProxy
import zxingcpp.BarcodeReader
import java.util.Locale

internal data class CameraBarcode(val text: String, val format: String, val symbology: String? = null)
internal data class BarcodeFrame(val barcode: CameraBarcode?)

internal class BarcodeDecoder {
    private val reader = BarcodeReader(BarcodeReader.Options(
        formats = setOf(
            BarcodeReader.Format.EAN_8, BarcodeReader.Format.EAN_13,
            BarcodeReader.Format.UPC_A, BarcodeReader.Format.UPC_E,
            BarcodeReader.Format.CODE_128, BarcodeReader.Format.CODE_39, BarcodeReader.Format.CODE_93,
            BarcodeReader.Format.ITF, BarcodeReader.Format.CODABAR,
            BarcodeReader.Format.QR_CODE, BarcodeReader.Format.DATA_MATRIX,
            BarcodeReader.Format.PDF_417, BarcodeReader.Format.AZTEC,
            BarcodeReader.Format.DATA_BAR, BarcodeReader.Format.DATA_BAR_EXPANDED, BarcodeReader.Format.DATA_BAR_LIMITED
        ),
        tryRotate = true,
        maxNumberOfSymbols = 2,
        minLineCount = 2,
        tryCode39ExtendedMode = false,
        textMode = BarcodeReader.TextMode.PLAIN
    ))
    private val lighting = BarcodeLighting()
    private var luma = ByteArray(0)
    private var pixels = IntArray(0)
    private var corrected: Bitmap? = null
    private var nextRecoveryAt = 0L
    private var recoveryCount = 0
    private val confirmedMiss = BarcodeFrame(null)

    // A skipped recovery isn't evidence that a held barcode left the frame.
    fun decode(frame: ImageProxy, now: Long): BarcodeFrame? {
        val viewport = frame.cropRect
        if (viewport.width() < 80 || viewport.height() < 60) return null
        val rotation = frame.imageInfo.rotationDegrees
        val turned = rotation % 180 != 0
        val width = if (turned) viewport.height() else viewport.width()
        val height = if (turned) viewport.width() else viewport.height()
        reader.options.tryHarder = false
        reader.options.tryInvert = false
        reader.options.tryDownscale = false
        select(reader.read(frame), width, height)?.let {
            nextRecoveryAt = 0
            recoveryCount = 0
            return BarcodeFrame(it)
        }
        if (now < nextRecoveryAt) return null
        nextRecoveryAt = now + 120
        reader.options.tryHarder = true
        reader.options.tryDownscale = true
        if (recoveryCount++ % 3 == 2) {
            // Preserve a regular thorough pass on the unmodified image for inverted labels.
            reader.options.tryInvert = true
            return select(reader.read(frame), width, height)?.let(::BarcodeFrame)
        }
        val image = recoverShadows(frame, viewport) ?: return null
        return select(reader.read(image, rotation = rotation), width, height)?.let(::BarcodeFrame) ?: confirmedMiss
    }

    private fun recoverShadows(frame: ImageProxy, crop: Rect): Bitmap? {
        val plane = frame.planes[0]
        if (plane.pixelStride != 1) return null
        val buffer = plane.buffer.duplicate()
        val width = crop.width()
        val height = crop.height()
        val start = buffer.position() + crop.top * plane.rowStride + crop.left
        if (start + (height - 1) * plane.rowStride + width > buffer.limit()) return null
        val count = width * height
        if (luma.size < count) {
            luma = ByteArray(count)
            pixels = IntArray(count)
        }
        for (y in 0 until height) {
            buffer.position(start + y * plane.rowStride)
            buffer.get(luma, y * width, width)
        }
        lighting.correct(luma, width, height, pixels)
        val bitmap = corrected?.takeIf { it.width == width && it.height == height }
            ?: Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888).also {
                corrected?.recycle()
                corrected = it
            }
        bitmap.setPixels(pixels, 0, width, 0, 0, width, height)
        return bitmap
    }

    private fun select(results: List<BarcodeReader.Result>, width: Int, height: Int): CameraBarcode? {
        val decoded = results.asSequence()
            .filter { it.error == null && !it.text.isNullOrBlank() }
            .filter {
                val p = it.position
                val coordinates = intArrayOf(p.topLeft.x, p.topLeft.y, p.topRight.x, p.topRight.y,
                    p.bottomRight.x, p.bottomRight.y, p.bottomLeft.x, p.bottomLeft.y)
                isCompleteBarcode(coordinates, width, height)
            }
            .minByOrNull {
                val p = it.position
                val dx = (p.topLeft.x.toDouble() + p.bottomRight.x - width) / width
                val dy = (p.topLeft.y.toDouble() + p.bottomRight.y - height) / height
                dx * dx + dy * dy
            }
        return decoded?.let { CameraBarcode(it.text!!, it.format.name.lowercase(Locale.ROOT), it.symbologyIdentifier) }
    }
}

internal fun isCompleteBarcode(points: IntArray, width: Int, height: Int): Boolean {
    if (points.size != 8 || width <= 0 || height <= 0) return false
    val padding = maxOf(4, minOf(width, height) / 50)
    return (0 until 8 step 2).all { index ->
        points[index] in padding until width - padding &&
            points[index + 1] in padding until height - padding
    }
}

/** Requires a second frame for formats that can lack strong checksum validation. */
internal class CameraScanGate {
    private var lastSent: CameraBarcode? = null
    private var candidate: CameraBarcode? = null
    private var seen = 0
    private var missingAt: Long? = null
    private val checkedFormats = setOf("ean_8", "ean_13", "upc_a", "upc_e", "code_128", "code_93",
        "qr_code", "data_matrix", "pdf_417", "aztec")

    fun observe(barcode: CameraBarcode?, now: Long): Boolean {
        if (barcode == null) {
            candidate = null
            seen = 0
            val missing = missingAt ?: now.also { missingAt = it }
            if (now - missing >= 340) lastSent = null
            return false
        }
        missingAt = null
        if (barcode == lastSent) {
            candidate = null
            seen = 0
            return false
        }
        if (barcode.format in checkedFormats) return true
        if (barcode != candidate) {
            candidate = barcode
            seen = 1
            return false
        }
        seen = minOf(seen + 1, 2)
        return seen == 2
    }

    fun submitted(barcode: CameraBarcode) {
        lastSent = barcode
        candidate = null
        seen = 0
    }

    fun reset() {
        lastSent = null
        candidate = null
        seen = 0
        missingAt = null
    }
}
