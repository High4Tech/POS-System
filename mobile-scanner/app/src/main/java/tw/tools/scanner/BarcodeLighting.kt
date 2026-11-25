package tw.tools.scanner

import kotlin.math.abs

/** Estimates the paper brightness near each pixel without averaging across sharp shadows. */
internal class BarcodeLighting {
    private var rowWhite = ByteArray(0)
    private var forward = IntArray(0)
    private var queue = IntArray(0)

    fun correct(luma: ByteArray, width: Int, height: Int, pixels: IntArray) {
        val size = width * height
        if (rowWhite.size < size) rowWhite = ByteArray(size)
        val longest = maxOf(width, height)
        if (forward.size < longest) {
            forward = IntArray(longest)
            queue = IntArray(longest)
        }
        val radius = (minOf(width, height) / 16).coerceIn(12, 36)
        for (row in 0 until height) {
            val start = row * width
            localWhite(luma, start, 1, width, radius)
            for (x in 0 until width) rowWhite[start + x] = forward[x].toByte()
        }
        for (x in 0 until width) {
            localWhite(luma, x, width, height, radius)
            for (y in 0 until height) {
                val index = y * width + x
                val input = luma[index].toInt() and 255
                val white = maxOf(rowWhite[index].toInt() and 255, forward[y])
                // Cap gain in very dark areas so sensor noise stays below the black level.
                val gray = (255 - (white - input) * 640 / maxOf(white, 48)).coerceIn(0, 255)
                pixels[index] = 0xFF000000.toInt() or (gray * 0x010101)
            }
        }
    }

    private fun localWhite(luma: ByteArray, start: Int, stride: Int, length: Int, radius: Int) {
        sweep(luma, start, stride, 0, length, 1, radius)
        sweep(luma, start, stride, length - 1, -1, -1, radius)
    }

    private fun sweep(luma: ByteArray, start: Int, stride: Int, first: Int, end: Int, step: Int, radius: Int) {
        var head = 0
        var tail = 0
        var index = first
        while (index != end) {
            while (head < tail && abs(index - queue[head]) > radius) head++
            val value = luma[start + index * stride].toInt() and 255
            while (head < tail && (luma[start + queue[tail - 1] * stride].toInt() and 255) <= value) tail--
            queue[tail++] = index
            val peak = luma[start + queue[head] * stride].toInt() and 255
            forward[index] = if (step > 0) peak else minOf(forward[index], peak)
            index += step
        }
    }
}
