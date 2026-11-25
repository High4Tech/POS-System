package tw.tools.scanner

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.net.InetAddress

class ScannerTests {
    @Test fun digitalLinkPreservesMedicineIdentityAndRejectsInjection() {
        val link = "https://id.example.com/products/01/09524000059109/10/ABC%2B1/21/S123?17=271231"
        assertTrue(Gs1Barcode.marked(link, null))
        val parsed = Gs1Barcode.parse(link, 2026)!!
        assertEquals("09524000059109", parsed.gtin)
        assertEquals("ABC+1", parsed.batch)
        assertEquals("S123", parsed.serialNumber)
        assertEquals("2027-12-31", parsed.expiryDate)
        assertNull(Gs1Barcode.parse("https://example.com/01/09524000059109/10/LOT%1D17271231", 2026))
        assertNull(Gs1Barcode.parse("https://example.com/01/09524000059109?17=271231&17=281231", 2026))
        assertFalse(Gs1Barcode.marked("https://example.com/shop", null))
    }
    private val pin = "ABCDEF01".repeat(8)
    private val code = "01234567".repeat(6)
    private val base = "techpos://pair?v=1&host=192.168.42.10&port=8765&pin=$pin&code=$code"

    @Test fun pairingKeepsTransportIdentityAndSupportsLegacyWifi() {
        assertEquals(Method.WIFI, PairingLink.parse(base)?.address?.method)
        for (method in Method.values()) {
            val pairing = PairingLink.parse("$base&method=${method.id}")!!
            assertEquals(method, pairing.address.method)
            assertEquals("192.168.42.10", pairing.address.host)
            assertEquals(code, pairing.code)
            assertEquals(pairing, PairingLink.fromFields("192.168.42.10", "8765", pin, code, method))
        }
    }

    @Test fun pairingRejectsPublicAddressesCredentialsAndAmbiguousLinks() {
        assertNull(PairingLink.parse(base.replace("192.168.42.10", "8.8.8.8")))
        assertNull(PairingLink.parse(base.replace("192.168.42.10", "scanner.example")))
        assertNull(PairingLink.parse(base.replace("192.168.42.10", "192.0168.42.10")))
        assertNull(PairingLink.parse("$base&host=192.168.1.1"))
        assertNull(PairingLink.parse("$base&method=unknown"))
        assertNull(PairingLink.parse("$base#fragment"))
        assertNull(PairingLink.parse(base.replace("techpos://pair", "techpos://user@pair")))
        assertNull(PairingLink.parse(base.replace(pin, "Z".repeat(64))))
    }

    @Test fun validatesSubnetAndDoesNotUseAnotherTransportInterface() {
        fun bytes(ip: String) = InetAddress.getByName(ip).address
        assertTrue(sameSubnet(bytes("192.168.42.129"), bytes("192.168.42.254"), 25))
        assertFalse(sameSubnet(bytes("192.168.42.129"), bytes("192.168.42.2"), 25))
        assertFalse(sameSubnet(bytes("192.168.42.129"), bytes("192.168.42.129"), 25))
        assertFalse(sameSubnet(bytes("192.168.42.129"), bytes("8.8.8.8"), 0))
        assertTrue(matchesInterface(Method.USB, "rndis0"))
        assertTrue(matchesInterface(Method.USB, "ncm0"))
        assertTrue(matchesInterface(Method.BLUETOOTH, "bt-pan"))
        assertTrue(matchesInterface(Method.BLUETOOTH, "bt_pan0"))
        assertTrue(matchesInterface(Method.BLUETOOTH, "bnep0"))
        assertFalse(matchesInterface(Method.USB, "bt-pan"))
        assertFalse(matchesInterface(Method.BLUETOOTH, "rndis0"))
        assertFalse(matchesInterface(Method.USB, "wlan0"))
    }

    @Test fun clippedSymbolsAreRejected() {
        assertTrue(isCompleteBarcode(intArrayOf(10, 10, 300, 11, 300, 150, 10, 150), 320, 240))
        assertFalse(isCompleteBarcode(intArrayOf(1, 10, 300, 11, 300, 150, 10, 150), 320, 240))
        assertFalse(isCompleteBarcode(intArrayOf(10, 10, 322, 11, 300, 150, 10, 150), 320, 240))
    }

    @Test fun confirmsUncheckedFormatsAcrossTwoFramesAndSuppressesHeldDuplicates() {
        val gate = CameraScanGate()
        val first = CameraBarcode("X003SWHW5T", "code_39")
        val second = CameraBarcode("5901234123457", "ean_13")
        val misread = CameraBarcode("@553S8EW5T", "code_39")
        assertFalse(gate.observe(first, 0))
        assertFalse(gate.observe(misread, 30))
        assertFalse(gate.observe(first, 60))
        assertTrue(gate.observe(first, 90))
        gate.submitted(first)
        assertFalse(gate.observe(first, 120))
        assertTrue(gate.observe(second, 150))
        assertTrue(gate.observe(second, 180))
        assertTrue(gate.observe(second, 210))
        gate.submitted(second)
        assertFalse(gate.observe(second, 240))
        assertFalse(gate.observe(null, 270))
        assertFalse(gate.observe(null, 400))
        assertFalse(gate.observe(second, 430))
        assertFalse(gate.observe(null, 500))
        assertFalse(gate.observe(null, 850))
        assertTrue(gate.observe(second, 880))
    }

    @Test fun correctsContrastOnBothSidesOfAShadowForEitherBarcodeOrientation() {
        val lighting = BarcodeLighting()
        val size = 160
        for (rotate in listOf(false, true)) {
            val strip = ByteArray(size * size)
            val output = IntArray(strip.size)
            for (y in 0 until size) for (x in 0 until size) {
                val i = if (rotate) y else x
                val bar = i in 16 until 144 && i % 8 in 0..2
                strip[y * size + x] = (if (i < 77) {
                    if (bar) 31 else 215
                } else {
                    if (bar) 34 else 63
                }).toByte()
            }
            lighting.correct(strip, size, size, output)
            for (i in 16 until 144) {
                val pixel = output[if (rotate) i * size + 90 else 90 * size + i] and 255
                val bar = i % 8 in 0..2
                assertTrue("Unexpected gray level at ${i}: ${pixel}", if (bar) pixel < 100 else pixel > 200)
            }
        }
    }

    @Test fun readsGs1WithoutAssumingFieldOrderAndRejectsOrdinary2dMetadata() {
        val gtin = "09521101530001"
        val raw = "21SN-4\u001d01${gtin}1728020010B-28"
        assertFalse(Gs1Barcode.marked(raw, "]Q1"))
        assertTrue(Gs1Barcode.marked(raw, "]Q3"))
        assertEquals(Gs1Barcode(gtin, "B-28", "2028-02-29", "SN-4"), Gs1Barcode.parse(raw, 2026))
        assertEquals(Gs1Barcode(gtin, "B-28", "2028-06-30", null),
            Gs1Barcode.parse("]d201${gtin}1728063010B-28", 2026))
        assertNull(Gs1Barcode.parse("]d201${gtin}17270229", 2026))
        assertNull(Gs1Barcode.parse("]d2010952110153000217280630", 2026))
    }
}
