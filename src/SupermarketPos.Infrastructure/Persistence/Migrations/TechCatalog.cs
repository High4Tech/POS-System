namespace SupermarketPos.Infrastructure.Persistence.Migrations;

internal static class TechCatalog
{
    public static readonly (string Category, (string Name, int Price)[] Products)[] Groups =
    {
        ("Phone Cases", new[]
        {
            ("Vivo Y18 Clear TPU Cover", 450),
            ("Vivo Y19s Shockproof Cover", 650),
            ("Vivo Y28 Matte Black Cover", 550),
            ("Vivo Y36 Silicone Cover", 650),
            ("Vivo V30 Slim Clear Cover", 750),
            ("Vivo V40 Soft Touch Cover", 850),
            ("Oppo A3x Clear TPU Cover", 450),
            ("Oppo A58 Matte Cover", 600),
            ("Oppo Reno11 Camera Guard Cover", 850),
            ("Samsung Galaxy A06 Clear Cover", 450),
            ("Samsung Galaxy A15 Wallet Cover", 950),
            ("Samsung Galaxy A16 Rugged Cover", 800),
            ("Samsung Galaxy A25 Silicone Cover", 750),
            ("Redmi Note 13 Clear Cover", 500),
            ("Redmi Note 14 Rugged Cover", 800),
            ("iPhone 11 Silicone Cover", 950),
            ("iPhone 13 Magnetic Clear Cover", 1450),
            ("iPhone 15 Magnetic Matte Cover", 1650),
        }),
        ("Screen Protectors", new[]
        {
            ("Vivo Y18 Tempered Glass", 350),
            ("Vivo Y28 Tempered Glass", 400),
            ("Vivo V30 Curved Screen Film", 650),
            ("Vivo V40 Curved Screen Film", 750),
            ("Oppo A3x Tempered Glass", 350),
            ("Oppo A58 Tempered Glass", 400),
            ("Galaxy A06 Tempered Glass", 350),
            ("Galaxy A15 Full-Cover Glass", 500),
            ("Galaxy A16 Full-Cover Glass", 550),
            ("Redmi Note 13 Tempered Glass", 450),
            ("iPhone 13 Privacy Glass", 850),
            ("iPhone 15 Privacy Glass", 950),
        }),
        ("Mobile Phones", new[]
        {
            ("Motorola Dual-SIM Keypad Phone", 6500),
            ("Motorola Large-Button Keypad Phone", 7800),
            ("Nokia 105 Keypad Phone", 6200),
            ("Nokia 106 Dual-SIM Keypad Phone", 7200),
            ("itel Compact Dual-SIM Keypad Phone", 4300),
            ("itel Big-Battery Keypad Phone", 5600),
            ("QMobile Dual-SIM Keypad Phone", 4200),
            ("Jazz Digit 4G Keypad Phone", 8500),
            ("Vivo Y18 Smartphone 4 GB / 128 GB", 35900),
            ("Vivo Y19s Smartphone 4 GB / 128 GB", 38900),
            ("Samsung Galaxy A06 4 GB / 64 GB", 32900),
            ("Redmi A3 Smartphone 4 GB / 128 GB", 28900),
        }),
        ("Wall & Car Chargers", new[]
        {
            ("USB-A Wall Charger 5 W", 550),
            ("Dual USB-A Wall Charger 12 W", 850),
            ("USB-A Fast Charger 18 W", 1000),
            ("USB-C PD Wall Charger 20 W", 1450),
            ("USB-C PPS Wall Charger 25 W", 1950),
            ("USB-C PD Wall Charger 30 W", 2350),
            ("USB-C GaN Charger 45 W", 3650),
            ("USB-C GaN Charger 65 W", 5250),
            ("Dual-Port USB-C and USB-A Charger 33 W", 2450),
            ("Dual USB-C Desk Charger 65 W", 6200),
            ("Dual USB-A Car Charger 12 W", 800),
            ("USB-C PD Car Charger 30 W", 1650),
            ("USB-C Car Charger 45 W", 2550),
            ("Qi Wireless Charging Pad 15 W", 2600),
        }),
        ("Cables & Adapters", new[]
        {
            ("USB-A to Micro-USB Cable 1 m", 300),
            ("USB-A to Micro-USB Cable 2 m", 450),
            ("USB-A to USB-C Cable 1 m", 450),
            ("USB-A to USB-C Braided Cable 2 m", 700),
            ("USB-C to USB-C PD Cable 60 W 1 m", 850),
            ("USB-C to USB-C PD Cable 100 W 2 m", 1650),
            ("USB-A to Lightning Cable 1 m", 650),
            ("USB-C to Lightning Cable 1 m", 1050),
            ("USB-C to USB-A OTG Adapter", 450),
            ("Micro-USB to USB-A OTG Adapter", 300),
            ("USB-C to 3.5 mm Audio Adapter", 800),
            ("3.5 mm AUX Cable 1.5 m", 350),
            ("HDMI Cable 4K 2 m", 850),
            ("USB-C to HDMI Adapter 4K", 2350),
        }),
        ("Power Banks", new[]
        {
            ("Pocket Power Bank 5,000 mAh", 1800),
            ("Dual USB Power Bank 10,000 mAh", 2800),
            ("USB-C PD Power Bank 10,000 mAh", 3900),
            ("Magnetic Power Bank 5,000 mAh", 4250),
            ("USB-C PD Power Bank 20,000 mAh", 5400),
            ("Power Bank 20,000 mAh with Built-in Cables", 5100),
            ("USB-C Power Bank 30,000 mAh", 7600),
            ("Laptop Power Bank 65 W 24,000 mAh", 13900),
        }),
        ("Audio & Earphones", new[]
        {
            ("Wired Earphones with Mic 3.5 mm", 650),
            ("USB-C Wired Earphones with Mic", 1150),
            ("In-Ear Bass Earphones 3.5 mm", 950),
            ("Bluetooth Neckband Earphones", 1850),
            ("True Wireless Earbuds with Charging Case", 2450),
            ("Noise-Cancelling Bluetooth Earbuds", 4400),
            ("Over-Ear Bluetooth Headphones", 3600),
            ("Wired Gaming Headset with Mic", 2800),
            ("Mini Bluetooth Speaker 5 W", 1700),
            ("Portable Bluetooth Speaker 10 W", 3100),
            ("Wired Lavalier Microphone 3.5 mm", 950),
            ("USB-C Wireless Lapel Microphone Kit", 3650),
        }),
        ("Computer Accessories", new[]
        {
            ("Wired USB Optical Mouse", 650),
            ("Wireless Silent Mouse 2.4 GHz", 1250),
            ("Full-Size Wired USB Keyboard", 1450),
            ("Wireless Keyboard and Mouse Kit", 2950),
            ("USB 3.0 Flash Drive 32 GB", 1100),
            ("USB 3.0 Flash Drive 64 GB", 1650),
            ("USB-C 4-Port Hub", 1950),
            ("USB Webcam 1080p with Mic", 4350),
            ("Foldable Aluminium Laptop Stand", 2400),
            ("Laptop Sleeve 15.6 inch", 1450),
        }),
    };

    public static string Barcode(int sequence)
    {
        // Use an in-store code without the register's 2-prefix for variable-weight barcodes.
        var body = $"040{sequence:D9}";
        var sum = body.Select((digit, index) => (digit - '0') * (index % 2 == 0 ? 1 : 3)).Sum();
        return body + (10 - sum % 10) % 10;
    }
}
