import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:mobile_scanner/mobile_scanner.dart';
import 'package:shared_preferences/shared_preferences.dart';

// ================================================================
//  電腦位址。要換一台就改這一行。
// ================================================================
const String kServerHost = '192.168.1.100:8765';

// ---------------------------------------------------------------- 配色

const kInk = Color(0xFF080B10);
const kPanel = Color(0xFF121822);
const kRaise = Color(0xFF1B2430);
const kLine = Color(0xFF27313F);
const kText = Color(0xFFE9EEF5);
const kMuted = Color(0xFF77839A);
const kSignal = Color(0xFFFFB020);
const kOk = Color(0xFF35D399);
const kBad = Color(0xFFFF6B6B);

const kMono = 'monospace';

void main() {
  runApp(const ScannerApp());
}

class ScannerApp extends StatelessWidget {
  const ScannerApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: '條碼掃描槍',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        brightness: Brightness.dark,
        scaffoldBackgroundColor: kInk,
        colorScheme: const ColorScheme.dark(primary: kSignal, surface: kPanel),
      ),
      home: const ScannerPage(),
    );
  }
}

// ---------------------------------------------------------------- 位址處理

String normalizeHost(String raw) {
  var s = raw.trim();
  if (s.isEmpty) return '';
  if (!s.startsWith('http://') && !s.startsWith('https://')) s = 'http://$s';
  while (s.endsWith('/')) {
    s = s.substring(0, s.length - 1);
  }
  final hostPart = s.split('://').last.split('/').first;
  if (!hostPart.contains(':')) s = '$s:8765';
  return s;
}

// ---------------------------------------------------------------- 上傳佇列

class _Job {
  _Job(this.code, this.format, this.newline);
  final String code;
  final String format;
  final bool newline;
}

/// 掃到的條碼先進佇列再一筆一筆送出。
/// 電腦沒開或斷線時不會掉資料，每兩秒重試，順序不變。
class Uploader {
  final List<_Job> _queue = [];
  bool _busy = false;

  void Function(String code)? onSent;
  void Function(int pending, String error)? onPending;

  int get pending => _queue.length;
  String get _base => normalizeHost(kServerHost);

  void enqueue(String code, String format, bool newline) {
    _queue.add(_Job(code, format, newline));
    _drain();
  }

  void clear() => _queue.clear();

  Future<void> _drain() async {
    if (_busy) return;
    _busy = true;
    while (_queue.isNotEmpty) {
      final job = _queue.first;
      final error = await _post(job);
      if (error == null) {
        _queue.removeAt(0);
        onSent?.call(job.code);
      } else {
        onPending?.call(_queue.length, error);
        await Future<void>.delayed(const Duration(seconds: 2));
      }
    }
    _busy = false;
  }

  Future<String?> _post(_Job job) async {
    final client = HttpClient()..connectionTimeout = const Duration(seconds: 4);
    try {
      final req = await client.postUrl(Uri.parse('$_base/scan'));
      req.headers.contentType =
          ContentType('application', 'json', charset: 'utf-8');

      // 一定要自己設 contentLength，否則 Dart 會改用 chunked 分塊傳輸、
      // 不送 Content-Length，對面的標準函式庫伺服器就讀到空 body。
      final body = utf8.encode(jsonEncode({
        'code': job.code,
        'format': job.format,
        'newline': job.newline,
      }));
      req.contentLength = body.length;
      req.add(body);

      final res = await req.close().timeout(const Duration(seconds: 6));
      final text = await res.transform(utf8.decoder).join();

      if (res.statusCode < 200 || res.statusCode >= 300) {
        return 'HTTP ${res.statusCode} · ${text.trim()}';
      }
      final data = jsonDecode(text) as Map<String, dynamic>;
      if (data['ok'] == true) return null;
      return data['detail']?.toString() ?? '電腦端拒絕';
    } on TimeoutException {
      return '電腦沒有回應';
    } catch (e) {
      return '連不到電腦';
    } finally {
      client.close(force: true);
    }
  }

  Future<(bool, String)> ping() async {
    final client = HttpClient()..connectionTimeout = const Duration(seconds: 3);
    try {
      final req = await client.getUrl(Uri.parse('$_base/ping'));
      final res = await req.close().timeout(const Duration(seconds: 4));
      final text = await res.transform(utf8.decoder).join();
      if (res.statusCode != 200) return (false, 'HTTP ${res.statusCode}');
      final mode = (jsonDecode(text) as Map<String, dynamic>)['output'];
      return (true, _describe(mode.toString()));
    } on SocketException catch (e) {
      return (false, '連不上：${e.osError?.message ?? e.message}');
    } on TimeoutException {
      return (false, '逾時，電腦沒有回應');
    } catch (e) {
      return (false, e.toString());
    } finally {
      client.close(force: true);
    }
  }

  String _describe(String mode) => switch (mode) {
        'keyboard' => '鍵盤輸入',
        'clip' => '剪貼簿',
        'none' => '僅顯示',
        _ => mode,
      };
}

// ---------------------------------------------------------------- 主畫面

class Scan {
  Scan(this.code, this.time, this.endsLine);
  final String code;
  final String time;

  /// 這筆是不是一組的最後一個（電腦端會多空一行）
  final bool endsLine;
}

class ScannerPage extends StatefulWidget {
  const ScannerPage({super.key});

  @override
  State<ScannerPage> createState() => _ScannerPageState();
}

class _ScannerPageState extends State<ScannerPage>
    with SingleTickerProviderStateMixin {
  final MobileScannerController _camera = MobileScannerController(
    facing: CameraFacing.back,
    detectionSpeed: DetectionSpeed.normal,
    detectionTimeoutMs: 400,
    // 只認一維條碼。想加回 QR：補上 BarcodeFormat.qrCode
    formats: const [
      BarcodeFormat.code128,
      BarcodeFormat.code39,
      BarcodeFormat.code93,
      BarcodeFormat.codabar,
      BarcodeFormat.ean13,
      BarcodeFormat.ean8,
      BarcodeFormat.upcA,
      BarcodeFormat.upcE,
      BarcodeFormat.itf,
    ],
  );

  final Uploader _uploader = Uploader();
  late final AnimationController _sweep = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 2400),
  )..repeat(reverse: true);

  String _statusText = '啟動中…';
  bool _statusOk = false;
  bool _testing = false;
  int _pending = 0;

  /// 幾筆換一行，設定裡可改
  int _groupSize = 2;

  /// 這一行目前已經掃了幾筆
  int _inLine = 0;

  final List<Scan> _rows = [];

  List<Offset> _corners = const [];
  Size _imageSize = Size.zero;
  Timer? _boxTimer;

  int _flashIndex = -1;
  Timer? _flashTimer;

  @override
  void initState() {
    super.initState();
    _uploader
      ..onSent = (code) {
        if (!mounted) return;
        setState(() {
          _pending = _uploader.pending;
          _statusOk = true;
          _statusText = '已送出';
        });
      }
      ..onPending = (count, error) {
        if (!mounted) return;
        setState(() {
          _pending = count;
          _statusOk = false;
          _statusText = error;
        });
      };
    _loadSettings();
    _test(popup: false);
  }

  Future<void> _loadSettings() async {
    final sp = await SharedPreferences.getInstance();
    if (!mounted) return;
    setState(() => _groupSize = sp.getInt('groupSize') ?? 2);
  }

  Future<void> _saveGroupSize(int n) async {
    final sp = await SharedPreferences.getInstance();
    await sp.setInt('groupSize', n);
  }

  @override
  void dispose() {
    _camera.dispose();
    _sweep.dispose();
    _boxTimer?.cancel();
    _flashTimer?.cancel();
    super.dispose();
  }

  // ---------------------------------------------------------- 連線測試

  Future<void> _test({bool popup = true}) async {
    setState(() {
      _testing = true;
      _statusText = '測試中…';
    });
    final (ok, detail) = await _uploader.ping();
    if (!mounted) return;
    setState(() {
      _testing = false;
      _statusOk = ok;
      _statusText = ok ? '連線正常 · $detail' : detail;
    });
    if (!popup) return;

    await showDialog<void>(
      context: context,
      builder: (context) => AlertDialog(
        backgroundColor: kPanel,
        title: Text(ok ? '連線正常' : '連線失敗',
            style: TextStyle(color: ok ? kOk : kBad, fontSize: 18)),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(normalizeHost(kServerHost),
                style: const TextStyle(
                    color: kText, fontFamily: kMono, fontSize: 15)),
            const SizedBox(height: 12),
            Text(ok ? '電腦端輸出方式：$detail' : detail,
                style:
                    const TextStyle(color: kMuted, fontSize: 14, height: 1.5)),
            if (!ok) ...[
              const SizedBox(height: 12),
              const Text(
                '確認：電腦有跑 server.py、手機和電腦同一個 Wi-Fi、'
                'Windows 防火牆放行 Python 的私人網路。',
                style: TextStyle(color: kMuted, fontSize: 13, height: 1.5),
              ),
            ],
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('關閉', style: TextStyle(color: kSignal)),
          ),
        ],
      ),
    );
  }

  // ---------------------------------------------------------- 設定

  Future<void> _openSettings() async {
    await showModalBottomSheet<void>(
      context: context,
      backgroundColor: kPanel,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(22)),
      ),
      builder: (context) => StatefulBuilder(
        builder: (context, setSheet) => Padding(
          padding: EdgeInsets.fromLTRB(
              22, 14, 22, 22 + MediaQuery.of(context).viewInsets.bottom),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Center(
                child: Container(
                  width: 38,
                  height: 4,
                  decoration: BoxDecoration(
                    color: kLine,
                    borderRadius: BorderRadius.circular(2),
                  ),
                ),
              ),
              const SizedBox(height: 20),
              const Text('設定',
                  style: TextStyle(
                      color: kText, fontSize: 19, fontWeight: FontWeight.w700)),
              const SizedBox(height: 4),
              Text(normalizeHost(kServerHost),
                  style: const TextStyle(
                      color: kMuted, fontFamily: kMono, fontSize: 12)),
              const SizedBox(height: 22),

              // 幾筆換一行
              const Text('掃幾筆空一行',
                  style: TextStyle(color: kText, fontSize: 15)),
              const SizedBox(height: 4),
              const Text('每筆條碼各自一行；滿這個數量後多空一行',
                  style: TextStyle(color: kMuted, fontSize: 12)),
              const SizedBox(height: 12),
              Row(
                children: [
                  _stepBtn(Icons.remove, () {
                    if (_groupSize <= 1) return;
                    setSheet(() {});
                    setState(() {
                      _groupSize--;
                      _inLine = 0;
                    });
                    _saveGroupSize(_groupSize);
                  }),
                  Expanded(
                    child: Center(
                      child: Text('$_groupSize',
                          style: const TextStyle(
                              color: kSignal,
                              fontSize: 34,
                              fontFamily: kMono,
                              fontWeight: FontWeight.w700)),
                    ),
                  ),
                  _stepBtn(Icons.add, () {
                    if (_groupSize >= 20) return;
                    setSheet(() {});
                    setState(() {
                      _groupSize++;
                      _inLine = 0;
                    });
                    _saveGroupSize(_groupSize);
                  }),
                ],
              ),
              const SizedBox(height: 22),

              Row(
                children: [
                  _sheetBtn('測試連線', Icons.wifi_tethering, () {
                    Navigator.pop(context);
                    _test();
                  }, highlight: true),
                  const SizedBox(width: 10),
                  _sheetBtn('清空清單', Icons.delete_outline, () {
                    _uploader.clear();
                    setState(() {
                      _rows.clear();
                      _pending = 0;
                      _flashIndex = -1;
                      _inLine = 0;
                    });
                    Navigator.pop(context);
                  }),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _stepBtn(IconData icon, VoidCallback onTap) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(14),
      child: Container(
        width: 58,
        height: 52,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: kRaise,
          border: Border.all(color: kLine),
          borderRadius: BorderRadius.circular(14),
        ),
        child: Icon(icon, color: kText, size: 22),
      ),
    );
  }

  Widget _sheetBtn(String label, IconData icon, VoidCallback onTap,
      {bool highlight = false}) {
    return Expanded(
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(14),
        child: Container(
          padding: const EdgeInsets.symmetric(vertical: 15),
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: highlight ? kSignal : kRaise,
            border: Border.all(color: highlight ? kSignal : kLine),
            borderRadius: BorderRadius.circular(14),
          ),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(icon, size: 18, color: highlight ? kInk : kText),
              const SizedBox(width: 8),
              Text(label,
                  style: TextStyle(
                      color: highlight ? kInk : kText,
                      fontSize: 15,
                      fontWeight: FontWeight.w600)),
            ],
          ),
        ),
      ),
    );
  }

  // ---------------------------------------------------------- 掃描

  void _onDetect(BarcodeCapture capture) {
    if (capture.barcodes.isEmpty) return;
    final barcode = capture.barcodes.first;

    // 先畫框。就算這筆被當成重複略過，框還是要出現，
    // 你才分得出「沒讀到」和「讀到但重複」。
    if (barcode.corners.length >= 4) {
      setState(() {
        _corners = barcode.corners;
        _imageSize = capture.size;
      });
      _boxTimer?.cancel();
      _boxTimer = Timer(const Duration(milliseconds: 1200), () {
        if (mounted) setState(() => _corners = const []);
      });
    }

    final value = barcode.rawValue;
    if (value == null || value.isEmpty) return;

    // 清單裡已經有就整筆略過。想重掃就把那一筆刪掉。
    final existing = _rows.indexWhere((r) => r.code == value);
    if (existing >= 0) {
      setState(() {
        _statusOk = true;
        _statusText = '已在清單第 ${_rows.length - existing} 筆，略過';
        _flashIndex = existing;
      });
      _flashTimer?.cancel();
      _flashTimer = Timer(const Duration(milliseconds: 1500), () {
        if (mounted) setState(() => _flashIndex = -1);
      });
      return;
    }

    HapticFeedback.mediumImpact();
    SystemSound.play(SystemSoundType.click);

    _inLine++;
    final endsLine = _inLine >= _groupSize;
    if (endsLine) _inLine = 0;

    final now = DateTime.now();
    setState(() {
      _flashIndex = -1;
      _rows.insert(
        0,
        Scan(
          value,
          '${now.hour.toString().padLeft(2, '0')}:'
          '${now.minute.toString().padLeft(2, '0')}:'
          '${now.second.toString().padLeft(2, '0')}',
          endsLine,
        ),
      );
    });

    _uploader.enqueue(value, barcode.format.name, endsLine);
  }

  // ---------------------------------------------------------- 版面

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Container(
        decoration: const BoxDecoration(
          gradient: LinearGradient(
            begin: Alignment.topCenter,
            end: Alignment.bottomCenter,
            colors: [Color(0xFF0E141D), kInk],
            stops: [0, 0.45],
          ),
        ),
        child: SafeArea(
          child: Column(
            children: [
              _header(),
              _viewfinder(),
              _lineIndicator(),
              Expanded(child: _history()),
            ],
          ),
        ),
      ),
    );
  }

  Widget _header() {
    return Padding(
      padding: const EdgeInsets.fromLTRB(14, 10, 6, 10),
      child: Row(
        children: [
          Expanded(
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
              decoration: BoxDecoration(
                color: kPanel,
                border: Border.all(color: _statusOk ? kOk : kBad, width: 1),
                borderRadius: BorderRadius.circular(99),
                boxShadow: [
                  BoxShadow(
                    color: (_statusOk ? kOk : kBad).withAlpha(38),
                    blurRadius: 14,
                    spreadRadius: 1,
                  ),
                ],
              ),
              child: Row(
                children: [
                  Container(
                    width: 8,
                    height: 8,
                    decoration: BoxDecoration(
                      color: _statusOk ? kOk : kBad,
                      shape: BoxShape.circle,
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(_statusText,
                        style: const TextStyle(color: kText, fontSize: 13),
                        overflow: TextOverflow.ellipsis),
                  ),
                  if (_pending > 0)
                    Text('待送 $_pending',
                        style: const TextStyle(
                            color: kBad, fontSize: 12, fontFamily: kMono)),
                ],
              ),
            ),
          ),
          IconButton(
            onPressed: _testing ? null : _openSettings,
            icon: Icon(Icons.settings,
                color: _testing ? kMuted : kSignal, size: 24),
          ),
        ],
      ),
    );
  }

  Widget _viewfinder() {
    return Padding(
      padding: const EdgeInsets.fromLTRB(14, 0, 14, 10),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(18),
        child: SizedBox(
          height: 210,
          child: Stack(
            alignment: Alignment.center,
            children: [
              ColoredBox(
                color: Colors.black,
                child: MobileScanner(
                  controller: _camera,
                  onDetect: _onDetect,
                  errorBuilder: (context, error, child) => Center(
                    child: Padding(
                      padding: const EdgeInsets.all(24),
                      child: Text('相機打不開：${error.errorCode.name}',
                          style:
                              const TextStyle(color: kMuted, fontSize: 14)),
                    ),
                  ),
                ),
              ),

              // 四角括號 + 來回掃的光線
              SizedBox(
                width: 290,
                height: 120,
                child: AnimatedBuilder(
                  animation: _sweep,
                  builder: (context, _) => CustomPaint(
                    painter: _FramePainter(progress: _sweep.value),
                  ),
                ),
              ),

              // 掃到的條碼實際位置
              Positioned.fill(
                child: IgnorePointer(
                  child: CustomPaint(
                    painter:
                        _BoxPainter(corners: _corners, imageSize: _imageSize),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  /// 這一行還差幾筆才換行，用小方塊表示
  Widget _lineIndicator() {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 10),
      child: Row(
        children: [
          Text('${_rows.length} 筆',
              style: const TextStyle(
                  color: kMuted,
                  fontSize: 11,
                  fontFamily: kMono,
                  letterSpacing: 1.2)),
          const Spacer(),
          const Text('本組 ',
              style: TextStyle(color: kMuted, fontSize: 11)),
          for (var i = 0; i < _groupSize; i++)
            Container(
              width: 9,
              height: 9,
              margin: const EdgeInsets.only(left: 5),
              decoration: BoxDecoration(
                color: i < _inLine ? kSignal : Colors.transparent,
                border: Border.all(
                    color: i < _inLine ? kSignal : kLine, width: 1.5),
                borderRadius: BorderRadius.circular(2),
              ),
            ),
        ],
      ),
    );
  }

  Widget _history() {
    if (_rows.isEmpty) {
      return const Center(
        child: Text('尚未掃描',
            style: TextStyle(color: kMuted, fontSize: 13, fontFamily: kMono)),
      );
    }
    return ListView.builder(
      padding: const EdgeInsets.only(bottom: 12),
      itemCount: _rows.length,
      itemBuilder: (context, i) {
        final row = _rows[i];
        final flash = i == _flashIndex;
        return Column(
          children: [
            Container(
              margin: const EdgeInsets.fromLTRB(12, 3, 12, 3),
              padding: const EdgeInsets.fromLTRB(12, 8, 4, 8),
              decoration: BoxDecoration(
                color: flash
                    ? const Color.fromRGBO(255, 176, 32, 0.20)
                    : kPanel,
                border: Border.all(color: flash ? kSignal : kLine),
                borderRadius: BorderRadius.circular(12),
              ),
              child: Row(
                children: [
                  Container(
                    width: 30,
                    height: 30,
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      color: kRaise,
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Text('${_rows.length - i}',
                        style: const TextStyle(
                            color: kSignal,
                            fontSize: 12,
                            fontFamily: kMono,
                            fontWeight: FontWeight.w700)),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(row.code,
                            style: const TextStyle(
                                color: kText,
                                fontSize: 20,
                                fontFamily: kMono,
                                fontWeight: FontWeight.w600)),
                        Text(row.time,
                            style: const TextStyle(
                                color: kMuted,
                                fontSize: 11,
                                fontFamily: kMono)),
                      ],
                    ),
                  ),
                  IconButton(
                    icon: const Icon(Icons.close, size: 19, color: kMuted),
                    onPressed: () => setState(() => _rows.removeAt(i)),
                  ),
                ],
              ),
            ),
            // 這筆之後電腦多空了一行，畫面上也標出來
            if (row.endsLine)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 20),
                child: Row(
                  children: [
                    Expanded(child: Container(height: 1, color: kLine)),
                    const Padding(
                      padding: EdgeInsets.symmetric(horizontal: 8),
                      child: Text('空行',
                          style: TextStyle(color: kMuted, fontSize: 11)),
                    ),
                    Expanded(child: Container(height: 1, color: kLine)),
                  ],
                ),
              ),
          ],
        );
      },
    );
  }
}

// ---------------------------------------------------------------- 取景框

/// 四角括號加一條來回掃的光線。
class _FramePainter extends CustomPainter {
  const _FramePainter({required this.progress});

  final double progress;

  @override
  void paint(Canvas canvas, Size size) {
    const arm = 26.0;
    final stroke = Paint()
      ..color = kSignal
      ..style = PaintingStyle.stroke
      ..strokeWidth = 3
      ..strokeCap = StrokeCap.round;

    void corner(double x, double y, double dx, double dy) {
      canvas.drawLine(Offset(x, y), Offset(x + arm * dx, y), stroke);
      canvas.drawLine(Offset(x, y), Offset(x, y + arm * dy), stroke);
    }

    corner(0, 0, 1, 1);
    corner(size.width, 0, -1, 1);
    corner(0, size.height, 1, -1);
    corner(size.width, size.height, -1, -1);

    // 來回掃的光線
    final y = size.height * (0.08 + 0.84 * progress);
    canvas.drawLine(
      Offset(6, y),
      Offset(size.width - 6, y),
      Paint()
        ..shader = const LinearGradient(
          colors: [
            Color.fromRGBO(255, 176, 32, 0),
            Color.fromRGBO(255, 176, 32, 0.9),
            Color.fromRGBO(255, 176, 32, 0),
          ],
        ).createShader(Rect.fromLTWH(0, 0, size.width, size.height))
        ..strokeWidth = 2,
    );
  }

  @override
  bool shouldRepaint(_FramePainter old) => old.progress != progress;
}

// ---------------------------------------------------------------- 條碼框線

/// 把掃到的條碼四個角畫成綠框。
///
/// 相機給的是「影像」座標、畫面是「widget」座標，尺寸不同，
/// 而且 MobileScanner 預設用 cover 填滿（影像會被裁掉一部分），
/// 所以要先算出縮放倍率和裁切位移才對得準。
class _BoxPainter extends CustomPainter {
  const _BoxPainter({required this.corners, required this.imageSize});

  final List<Offset> corners;
  final Size imageSize;

  @override
  void paint(Canvas canvas, Size size) {
    if (corners.length < 4 || imageSize.isEmpty) return;

    final scale = math.max(
      size.width / imageSize.width,
      size.height / imageSize.height,
    );
    final offsetX = (size.width - imageSize.width * scale) / 2;
    final offsetY = (size.height - imageSize.height * scale) / 2;

    final path = Path();
    for (var i = 0; i < corners.length; i++) {
      final p = Offset(
        corners[i].dx * scale + offsetX,
        corners[i].dy * scale + offsetY,
      );
      if (i == 0) {
        path.moveTo(p.dx, p.dy);
      } else {
        path.lineTo(p.dx, p.dy);
      }
    }
    path.close();

    canvas.drawPath(
      path,
      Paint()
        ..color = const Color.fromRGBO(53, 211, 153, 0.20)
        ..style = PaintingStyle.fill,
    );
    canvas.drawPath(
      path,
      Paint()
        ..color = kOk
        ..style = PaintingStyle.stroke
        ..strokeWidth = 3
        ..strokeJoin = StrokeJoin.round,
    );
  }

  @override
  bool shouldRepaint(_BoxPainter old) =>
      old.corners != corners || old.imageSize != imageSize;
}
