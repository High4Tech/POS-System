package tw.tools.scanner

import java.util.Calendar
import java.util.GregorianCalendar
import java.util.Locale
import java.net.URI
import java.net.URLDecoder

internal data class Gs1Barcode(val gtin: String, val batch: String?, val expiryDate: String?, val serialNumber: String?) {
    companion object {
        private const val separator = '\u001d'
        private val markers = setOf("]d2", "]Q3", "]C1")
        private val printed = Regex("\\((\\d{2,4})\\)")
        private fun size(ai: String) = when (ai) {
            "01" -> 14
            "11", "13", "15", "16", "17" -> 6
            else -> 0
        }

        fun marked(raw: String, symbology: String?) = symbology in markers ||
            markers.any { raw.startsWith(it) } || raw.startsWith(separator) ||
            digitalLink(raw) || symbology == null && raw.startsWith('(') && "(01)" in raw

        fun parse(raw: String, currentYear: Int = GregorianCalendar().get(Calendar.YEAR)): Gs1Barcode? {
            if (digitalLink(raw)) return parseLink(raw, currentYear)
            val data = (if (markers.any { raw.startsWith(it) }) raw.drop(3) else raw).removePrefix(separator.toString())
            val fields = mutableMapOf<String, String>()
            if (data.startsWith('(')) {
                val codes = printed.findAll(data).toList()
                if (codes.firstOrNull()?.range?.first != 0) return null
                for ((i, match) in codes.withIndex()) {
                    val value = data.substring(match.range.last + 1, codes.getOrNull(i + 1)?.range?.first ?: data.length)
                    if (fields.put(match.groupValues[1], value) != null) return null
                }
            } else {
                var offset = 0
                while (offset < data.length) {
                    if (offset + 2 > data.length) return null
                    val ai = data.substring(offset, offset + 2)
                    offset += 2
                    val length = size(ai)
                    if (length == 0 && ai != "10" && ai != "21") return null
                    val end = if (length == 0) data.indexOf(separator, offset).takeIf { it >= 0 } ?: data.length
                        else offset + length
                    if (end <= offset || end > data.length || fields.put(ai, data.substring(offset, end)) != null) return null
                    offset = end
                    if (data.getOrNull(offset) == separator) offset++
                }
            }
            for ((ai, value) in fields) {
                val length = size(ai)
                if (length != 0) {
                    if (value.length != length || value.any { it !in '0'..'9' }) return null
                } else if ((ai != "10" && ai != "21") || value.length !in 1..20 ||
                    value.any { it !in '0'..'9' && it !in 'A'..'Z' && it !in 'a'..'z' && it !in "!\"%&'()*+,-./:;<=>?_" }) return null
            }
            val gtin = fields["01"] ?: return null
            val sum = gtin.dropLast(1).reversed().withIndex().sumOf { (i, digit) ->
                (digit - '0') * if (i % 2 == 0) 3 else 1
            }
            if ((10 - sum % 10) % 10 != gtin.last() - '0') return null
            val expiry = fields["17"]?.let { day(it, currentYear) ?: return null }
            return Gs1Barcode(gtin, fields["10"], expiry, fields["21"])
        }

        private fun digitalLink(raw: String): Boolean = runCatching {
            val uri = URI(raw)
            uri.scheme in listOf("http", "https") && uri.rawPath.orEmpty().split('/').contains("01")
        }.getOrDefault(false)

        private fun parseLink(raw: String, year: Int): Gs1Barcode? = runCatching {
            val uri = URI(raw)
            if (raw.length > 512 || uri.rawUserInfo != null) return null
            fun decode(value: String) = URLDecoder.decode(value.replace("+", "%2B"), "UTF-8")
            val path = uri.rawPath.trim('/').split('/')
            val start = path.indexOf("01")
            if (start < 0 || (path.size - start) % 2 != 0) return null
            val fields = linkedMapOf<String, String>()
            var last = 0
            for (i in start until path.size step 2) {
                val ai = path[i]
                val order = when (ai) { "01" -> 1; "10" -> 2; "21" -> 3; else -> 0 }
                if (order <= last || fields.put(ai, decode(path[i + 1])) != null) return null
                last = order
            }
            for (part in uri.rawQuery.orEmpty().split('&', ';')) {
                if (part.isEmpty()) continue
                val pair = part.split('=', limit = 2)
                val key = decode(pair[0])
                if (key.any { it !in '0'..'9' }) continue
                if (pair.size != 2 || key !in listOf("11", "13", "15", "16", "17") || fields.put(key, decode(pair[1])) != null) return null
            }
            if (fields.values.any { value -> value.any { it.isISOControl() } }) return null
            parse(fields.entries.joinToString(separator.toString()) { it.key + it.value }, year)
        }.getOrNull()

        private fun day(text: String, currentYear: Int): String? {
            var year = currentYear / 100 * 100 + text.substring(0, 2).toInt()
            if (year - currentYear > 50) year -= 100
            if (currentYear - year > 49) year += 100
            val month = text.substring(2, 4).toInt()
            val day = text.substring(4).toInt()
            if (month !in 1..12) return null
            val calendar = GregorianCalendar(year, month - 1, 1)
            val last = calendar.getActualMaximum(Calendar.DAY_OF_MONTH)
            if (day > last) return null
            return String.format(Locale.ROOT, "%04d-%02d-%02d", year, month, if (day == 0) last else day)
        }
    }
}
