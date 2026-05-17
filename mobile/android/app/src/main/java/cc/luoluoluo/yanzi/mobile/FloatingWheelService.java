package cc.luoluoluo.yanzi.mobile;

import android.app.Service;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.Point;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.IBinder;
import android.provider.Settings;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.view.inputmethod.InputMethodManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.Base64;
import java.util.Date;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class FloatingWheelService extends Service {
    private static final String DEFAULT_BASE_URL = "https://sync.luoluoluo.cc.cd";

    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private WindowManager windowManager;
    private View bubbleView;
    private View wheelView;
    private View panelView;
    private View progressView;
    private SharedPreferences prefs;
    private int startX;
    private int startY;
    private float touchStartX;
    private float touchStartY;

    @Override
    public void onCreate() {
        super.onCreate();
        prefs = getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE);
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
        showBubble();
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (bubbleView == null) {
            showBubble();
        }
        return START_STICKY;
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @Override
    public void onDestroy() {
        removeView(wheelView);
        removeView(panelView);
        removeView(progressView);
        removeView(bubbleView);
        executor.shutdownNow();
        super.onDestroy();
    }

    private void showBubble() {
        if (!Settings.canDrawOverlays(this) || windowManager == null || bubbleView != null) {
            return;
        }

        ImageView bubble = new ImageView(this);
        bubble.setImageResource(getResources().getIdentifier("yanzi_launcher_bitmap", "drawable", getPackageName()));
        bubble.setBackground(circleDrawable(Color.rgb(5, 8, 13), Color.rgb(34, 211, 238), 1));
        bubble.setPadding(dp(8), dp(8), dp(8), dp(8));
        bubble.setScaleType(ImageView.ScaleType.CENTER_INSIDE);

        WindowManager.LayoutParams params = overlayParams(58, 58);
        params.gravity = Gravity.TOP | Gravity.START;
        params.x = prefs.getInt("floatingBubbleX", 24);
        params.y = prefs.getInt("floatingBubbleY", 240);

        bubble.setOnTouchListener((view, event) -> {
            switch (event.getAction()) {
                case MotionEvent.ACTION_DOWN:
                    startX = params.x;
                    startY = params.y;
                    touchStartX = event.getRawX();
                    touchStartY = event.getRawY();
                    return true;
                case MotionEvent.ACTION_MOVE:
                    params.x = startX + (int) (event.getRawX() - touchStartX);
                    params.y = startY + (int) (event.getRawY() - touchStartY);
                    windowManager.updateViewLayout(view, params);
                    return true;
                case MotionEvent.ACTION_UP:
                    prefs.edit().putInt("floatingBubbleX", params.x).putInt("floatingBubbleY", params.y).apply();
                    if (Math.abs(event.getRawX() - touchStartX) < 8 && Math.abs(event.getRawY() - touchStartY) < 8) {
                        toggleWheel(params.x, params.y);
                    }
                    return true;
                default:
                    return false;
            }
        });

        bubbleView = bubble;
        windowManager.addView(bubbleView, params);
    }

    private void toggleWheel(int x, int y) {
        if (wheelView != null) {
            closeOverlayUi();
            return;
        }

        FrameLayout wheel = new FrameLayout(this);
        wheel.setBackground(circleDrawable(Color.argb(222, 4, 12, 24), Color.argb(120, 34, 211, 238), 1));
        addWheelButton(wheel, "文本", 210, 24, () -> showTextPanel());
        addWheelButton(wheel, "截图", 338, 77, () -> sendScreenshotToDesktop());
        addWheelButton(wheel, "扩展", 390, 210, () -> openMain("extensions"));
        addWheelButton(wheel, "添加", 338, 344, () -> showExtensionPanel());
        addWheelButton(wheel, "运行", 210, 396, () -> showExtensionPanel());
        addWheelButton(wheel, "燕幕", 83, 344, () -> openMain("yanm"));
        addWheelButton(wheel, "刷新", 30, 210, () -> openMain("refresh"));
        addWheelButton(wheel, "输入", 83, 77, () -> showTextPanel());
        addCenterLogo(wheel, () -> closeOverlayUi());

        WindowManager.LayoutParams params = overlayParams(516, 516);
        params.gravity = Gravity.TOP | Gravity.START;
        int wheelSize = dp(516);
        int bubbleSize = dp(58);
        int centerX = x + bubbleSize / 2;
        int centerY = y + bubbleSize / 2;
        Point displaySize = displaySize();
        params.x = clamp(centerX - wheelSize / 2, 0, Math.max(0, displaySize.x - wheelSize));
        params.y = clamp(centerY - wheelSize / 2, 0, Math.max(0, displaySize.y - wheelSize));
        wheelView = wheel;
        windowManager.addView(wheelView, params);
    }

    private void addWheelButton(FrameLayout wheel, String text, int left, int top, Runnable action) {
        TextView button = new TextView(this);
        button.setText(text);
        button.setTextColor(Color.WHITE);
        button.setTextSize(13);
        button.setGravity(Gravity.CENTER);
        button.setBackground(circleDrawable(Color.rgb(21, 94, 117), Color.argb(120, 125, 211, 252), 1));
        button.setOnClickListener(v -> action.run());
        FrameLayout.LayoutParams params = new FrameLayout.LayoutParams(dp(96), dp(96));
        params.leftMargin = dp(left);
        params.topMargin = dp(top);
        wheel.addView(button, params);
    }

    private void addCenterLogo(FrameLayout wheel, Runnable action) {
        ImageView logo = new ImageView(this);
        logo.setImageResource(getResources().getIdentifier("yanzi_launcher_bitmap", "drawable", getPackageName()));
        logo.setBackground(circleDrawable(Color.rgb(5, 8, 13), Color.argb(180, 34, 211, 238), 1));
        logo.setPadding(dp(15), dp(15), dp(15), dp(15));
        logo.setScaleType(ImageView.ScaleType.CENTER_INSIDE);
        logo.setOnClickListener(v -> action.run());
        FrameLayout.LayoutParams params = new FrameLayout.LayoutParams(dp(117), dp(117));
        params.leftMargin = dp(200);
        params.topMargin = dp(200);
        wheel.addView(logo, params);
    }

    private void showTextPanel() {
        removeView(panelView);
        LinearLayout panel = overlayPanel();
        panel.addView(panelTitle("发送到电脑"));
        EditText input = panelInput("输入要发送给电脑的文本", "燕子", 3);
        panel.addView(input);
        LinearLayout buttons = row();
        Button send = panelButton("发送");
        Button copy = panelButton("复制");
        Button close = panelButton("关闭");
        buttons.addView(send, new LinearLayout.LayoutParams(0, dp(42), 1));
        buttons.addView(copy, new LinearLayout.LayoutParams(0, dp(42), 1));
        buttons.addView(close, new LinearLayout.LayoutParams(0, dp(42), 1));
        panel.addView(buttons);

        send.setOnClickListener(v -> {
            String text = input.getText().toString().trim();
            if (text.isEmpty()) {
                text = "燕子";
                input.setText(text);
            }
            copyToClipboard("Yanzi mobile text", text);
            sendTextToDesktop(text);
        });
        copy.setOnClickListener(v -> copyToClipboard("Yanzi mobile text", input.getText().toString().trim().isEmpty() ? "燕子" : input.getText().toString()));
        close.setOnClickListener(v -> {
            closePanel();
        });
        showPanel(panel, 220);
        focusInput(input);
    }

    private void showExtensionPanel() {
        removeView(panelView);
        LinearLayout panel = overlayPanel();
        panel.addView(panelTitle("添加手机扩展"));
        EditText input = panelInput("粘贴 mobile-js 扩展 JSON", prefs.getString("mobileExtensionDraft", defaultMobileExtensionJson()), 8);
        panel.addView(input);
        LinearLayout buttons = row();
        Button save = panelButton("保存");
        Button prompt = panelButton("提示词");
        Button close = panelButton("关闭");
        buttons.addView(save, new LinearLayout.LayoutParams(0, dp(42), 1));
        buttons.addView(prompt, new LinearLayout.LayoutParams(0, dp(42), 1));
        buttons.addView(close, new LinearLayout.LayoutParams(0, dp(42), 1));
        panel.addView(buttons);

        save.setOnClickListener(v -> {
            prefs.edit().putString("mobileExtensionDraft", input.getText().toString()).apply();
            toast("手机扩展已保存，可点“运行”或回 App 调试。");
        });
        prompt.setOnClickListener(v -> copyToClipboard("Yanzi mobile extension prompt", mobileExtensionPrompt()));
        close.setOnClickListener(v -> {
            closePanel();
        });
        showPanel(panel, 420);
        focusInput(input);
    }

    private LinearLayout overlayPanel() {
        LinearLayout panel = new LinearLayout(this);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setFocusable(true);
        panel.setFocusableInTouchMode(true);
        panel.setPadding(dp(14), dp(12), dp(14), dp(12));
        panel.setOnKeyListener((v, keyCode, event) -> {
            if (keyCode == KeyEvent.KEYCODE_BACK && event.getAction() == KeyEvent.ACTION_UP) {
                closePanel();
                return true;
            }
            return false;
        });
        GradientDrawable background = new GradientDrawable();
        background.setColor(Color.argb(246, 6, 17, 31));
        background.setCornerRadius(dp(18));
        background.setStroke(dp(1), Color.argb(140, 34, 211, 238));
        panel.setBackground(background);
        return panel;
    }

    private void showPanel(View panel, int heightDp) {
        WindowManager.LayoutParams params = overlayParamsFocusable(-1, heightDp);
        params.gravity = Gravity.BOTTOM | Gravity.START;
        params.x = dp(12);
        params.y = dp(18);
        panelView = panel;
        windowManager.addView(panelView, params);
        panelView.requestFocus();
    }

    private void showProgress(String text) {
        removeView(progressView);
        LinearLayout panel = new LinearLayout(this);
        panel.setOrientation(LinearLayout.HORIZONTAL);
        panel.setGravity(Gravity.CENTER_VERTICAL);
        panel.setPadding(dp(14), dp(10), dp(14), dp(10));
        panel.setBackground(roundedRectDrawable(Color.argb(238, 6, 17, 31), Color.argb(160, 34, 211, 238), 16));
        TextView spinner = new TextView(this);
        spinner.setText("...");
        spinner.setTextColor(Color.rgb(34, 211, 238));
        spinner.setTextSize(18);
        TextView label = new TextView(this);
        label.setText(text);
        label.setTextColor(Color.WHITE);
        label.setTextSize(14);
        label.setPadding(dp(10), 0, 0, 0);
        panel.addView(spinner, new LinearLayout.LayoutParams(dp(34), dp(34)));
        panel.addView(label, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT));
        WindowManager.LayoutParams params = overlayParams(220, 56);
        params.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
        params.y = dp(92);
        progressView = panel;
        windowManager.addView(progressView, params);
    }

    private void hideProgress() {
        android.os.Handler handler = new android.os.Handler(getMainLooper());
        handler.post(() -> {
            removeView(progressView);
            progressView = null;
        });
    }

    private LinearLayout panelTitle(String text) {
        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);
        TextView title = new TextView(this);
        title.setText(text);
        title.setTextColor(Color.WHITE);
        title.setTextSize(16);
        title.setGravity(Gravity.START);
        title.setPadding(0, 0, 0, dp(8));
        Button close = panelButton("退出");
        close.setOnClickListener(v -> closePanel());
        header.addView(title, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));
        header.addView(close, new LinearLayout.LayoutParams(dp(76), dp(38)));
        return header;
    }

    private EditText panelInput(String hint, String value, int minLines) {
        EditText input = new EditText(this);
        input.setHint(hint);
        input.setText(value);
        input.setTextColor(Color.WHITE);
        input.setHintTextColor(Color.rgb(148, 163, 184));
        input.setMinLines(minLines);
        input.setGravity(Gravity.TOP);
        input.setSingleLine(false);
        input.setBackgroundColor(Color.rgb(15, 23, 42));
        input.setPadding(dp(10), dp(8), dp(10), dp(8));
        input.setOnKeyListener((v, keyCode, event) -> {
            if (keyCode == KeyEvent.KEYCODE_BACK && event.getAction() == KeyEvent.ACTION_UP) {
                closePanel();
                return true;
            }
            return false;
        });
        return input;
    }

    private LinearLayout row() {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setPadding(0, dp(10), 0, 0);
        return row;
    }

    private Button panelButton(String text) {
        Button button = new Button(this);
        button.setText(text);
        return button;
    }

    private void focusInput(EditText input) {
        input.requestFocus();
        input.postDelayed(() -> {
            InputMethodManager manager = (InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
            if (manager != null) {
                manager.showSoftInput(input, InputMethodManager.SHOW_IMPLICIT);
            }
        }, 180);
    }

    private void sendClipboardTextToDesktop() {
        ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        ClipData clip = clipboard == null ? null : clipboard.getPrimaryClip();
        CharSequence value = clip == null || clip.getItemCount() == 0 ? "" : clip.getItemAt(0).coerceToText(this);
        String text = value == null ? "" : value.toString().trim();
        if (text.isEmpty()) {
            openMain("compose-text");
            toast("系统限制后台读取剪贴板，请在输入框发送。");
            return;
        }
        sendTextToDesktop(text);
    }

    private void sendScreenshotToDesktop() {
        log("截图：用户点击截图。");
        if (!MobileAccessibilityService.isEnabled()) {
            log("截图：无障碍未开启，跳转设置。");
            openAccessibilitySettings();
            toast("请开启燕子无障碍服务后再使用截图。");
            return;
        }

        closeOverlayUi();
        showProgress("正在截图并发送...");
        log("截图：开始调用无障碍截图。");
        MobileAccessibilityService.captureJpegBase64(new MobileAccessibilityService.ScreenshotCallback() {
            @Override
            public void onSuccess(String jpegBase64, int width, int height) {
                log("截图：无障碍截图成功，尺寸=" + width + "x" + height + "。");
                sendScreenshotPayloadToDesktop(jpegBase64, width, height);
            }

            @Override
            public void onFailure(String message) {
                hideProgress();
                log("截图：无障碍截图失败，" + message);
                toast("截图失败：" + message);
            }
        });
    }

    private void sendTextToDesktop(String text) {
        executor.execute(() -> {
            try {
                String token = requireToken();
                String deviceId = getOrCreateDeviceId();
                String messageId;
                try {
                    registerDevice(normalizedBaseUrl(), token, deviceId, buildDeviceName());
                    messageId = postMessage(normalizedBaseUrl(), token, deviceId, text);
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = refreshToken();
                    registerDevice(normalizedBaseUrl(), token, deviceId, buildDeviceName());
                    messageId = postMessage(normalizedBaseUrl(), token, deviceId, text);
                }
                toast("已发送到电脑：" + messageId);
            } catch (Exception ex) {
                toast("发送失败：" + ex.getMessage());
            }
        });
    }

    private void sendScreenshotPayloadToDesktop(String jpegBase64, int width, int height) {
        executor.execute(() -> {
            try {
                String token = requireToken();
                String deviceId = getOrCreateDeviceId();
                byte[] imageBytes = Base64.getDecoder().decode(jpegBase64);
                log("截图：准备上传 WebDAV，bytes=" + imageBytes.length + "。");
                String messageId;
                try {
                    registerDevice(normalizedBaseUrl(), token, deviceId, buildDeviceName());
                    log("截图：设备注册完成，正在读取 WebDAV 配置。");
                    WebDavConfig webDav = fetchWebDavConfig(normalizedBaseUrl(), token);
                    log("截图：WebDAV 配置读取完成，开始上传。");
                    String remotePath = uploadScreenshotToWebDav(webDav, imageBytes);
                    log("截图：WebDAV 上传完成，path=" + remotePath + "。");
                    messageId = postScreenshotWebDavMessage(normalizedBaseUrl(), token, deviceId, remotePath, imageBytes.length, width, height);
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    log("截图：Token 过期，刷新后重试。");
                    token = refreshToken();
                    registerDevice(normalizedBaseUrl(), token, deviceId, buildDeviceName());
                    WebDavConfig webDav = fetchWebDavConfig(normalizedBaseUrl(), token);
                    String remotePath = uploadScreenshotToWebDav(webDav, imageBytes);
                    log("截图：WebDAV 重试上传完成，path=" + remotePath + "。");
                    messageId = postScreenshotWebDavMessage(normalizedBaseUrl(), token, deviceId, remotePath, imageBytes.length, width, height);
                }
                log("截图：消息已发送到云端，messageId=" + messageId + "。");
                toast("截图已发送到电脑：" + messageId);
                hideProgress();
            } catch (Exception ex) {
                log("截图：发送失败，" + ex.getMessage());
                toast("截图发送失败：" + ex.getMessage());
                hideProgress();
            }
        });
    }

    private void openMain(String action) {
        Intent intent = new Intent(this, MainActivity.class);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        intent.setAction("cc.luoluoluo.yanzi.mobile." + action);
        startActivity(intent);
    }

    private void openAccessibilitySettings() {
        Intent intent = new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        startActivity(intent);
    }

    private WindowManager.LayoutParams overlayParams(int widthDp, int heightDp) {
        int type = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
            ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
            : WindowManager.LayoutParams.TYPE_PHONE;
        WindowManager.LayoutParams params = new WindowManager.LayoutParams(
            dp(widthDp),
            dp(heightDp),
            type,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
            PixelFormat.TRANSLUCENT);
        params.gravity = Gravity.TOP | Gravity.START;
        return params;
    }

    private WindowManager.LayoutParams overlayParamsFocusable(int widthDp, int heightDp) {
        int type = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
            ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
            : WindowManager.LayoutParams.TYPE_PHONE;
        int width = widthDp < 0 ? WindowManager.LayoutParams.MATCH_PARENT : dp(widthDp);
        WindowManager.LayoutParams params = new WindowManager.LayoutParams(
            width,
            dp(heightDp),
            type,
            WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
            PixelFormat.TRANSLUCENT);
        params.softInputMode = WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE;
        return params;
    }

    private GradientDrawable circleDrawable(int fillColor, int strokeColor, int strokeDp) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setShape(GradientDrawable.OVAL);
        drawable.setColor(fillColor);
        drawable.setStroke(dp(strokeDp), strokeColor);
        return drawable;
    }

    private GradientDrawable roundedRectDrawable(int fillColor, int strokeColor, int radiusDp) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setColor(fillColor);
        drawable.setCornerRadius(dp(radiusDp));
        drawable.setStroke(dp(1), strokeColor);
        return drawable;
    }

    private void removeView(View view) {
        if (view == null || windowManager == null) {
            return;
        }
        try {
            windowManager.removeView(view);
        } catch (Exception ignored) {
        }
    }

    private void closePanel() {
        removeView(panelView);
        panelView = null;
    }

    private void closeOverlayUi() {
        closePanel();
        removeView(wheelView);
        wheelView = null;
    }

    private void toast(String message) {
        android.os.Handler handler = new android.os.Handler(getMainLooper());
        handler.post(() -> Toast.makeText(this, message, Toast.LENGTH_SHORT).show());
    }

    private void log(String message) {
        MobileDiagnostics.append(this, message);
    }

    private void copyToClipboard(String label, String text) {
        ClipboardManager manager = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        if (manager != null) {
            manager.setPrimaryClip(ClipData.newPlainText(label, text == null || text.trim().isEmpty() ? "燕子" : text));
            toast("已复制到剪贴板");
        }
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density + 0.5f);
    }

    private Point displaySize() {
        Point size = new Point();
        if (windowManager != null) {
            windowManager.getDefaultDisplay().getSize(size);
        }
        if (size.x <= 0 || size.y <= 0) {
            size.x = getResources().getDisplayMetrics().widthPixels;
            size.y = getResources().getDisplayMetrics().heightPixels;
        }
        return size;
    }

    private static int clamp(int value, int min, int max) {
        return Math.max(min, Math.min(max, value));
    }

    private String normalizedBaseUrl() {
        String value = prefs.getString("baseUrl", DEFAULT_BASE_URL);
        if (value == null || value.trim().isEmpty()) {
            return DEFAULT_BASE_URL;
        }
        value = value.trim();
        int v1Index = value.indexOf("/v1/");
        if (v1Index >= 0) {
            value = value.substring(0, v1Index);
        }
        while (value.endsWith("/")) {
            value = value.substring(0, value.length() - 1);
        }
        return value.isEmpty() ? DEFAULT_BASE_URL : value;
    }

    private String getOrCreateDeviceId() {
        String existing = prefs.getString("deviceId", null);
        if (existing != null && !existing.trim().isEmpty()) {
            return existing;
        }
        String created = "android-" + UUID.randomUUID();
        prefs.edit().putString("deviceId", created).apply();
        return created;
    }

    private String buildDeviceName() {
        String maker = Build.MANUFACTURER == null ? "" : Build.MANUFACTURER.trim();
        String model = Build.MODEL == null ? "" : Build.MODEL.trim();
        String name = (maker + " " + model).trim();
        return name.isEmpty() ? "Android 手机" : name;
    }

    private String requireToken() {
        String token = prefs.getString("token", "");
        if (token == null || token.trim().isEmpty()) {
            return refreshToken();
        }
        return token;
    }

    private String refreshToken() {
        try {
            String email = prefs.getString("email", "");
            String password = prefs.getString("password", "");
            if (email == null || email.trim().isEmpty() || password == null || password.isEmpty()) {
                throw new IllegalStateException("请先在燕子移动端登录。");
            }

            String token = login(normalizedBaseUrl(), email.trim(), password);
            prefs.edit().putString("token", token).apply();
            return token;
        } catch (Exception ex) {
            throw new IllegalStateException("登录态已失效，请回到燕子移动端重新登录：" + ex.getMessage());
        }
    }

    private static boolean isUnauthorized(Exception ex) {
        String message = ex.getMessage();
        return message != null && message.contains("HTTP 401");
    }

    private static String login(String baseUrl, String email, String password) throws Exception {
        JSONObject payload = new JSONObject()
            .put("email", email)
            .put("password", password);
        return postJson(baseUrl, "/v1/auth/login", payload, null).getString("accessToken");
    }

    private static String mobileExtensionPrompt() {
        return "你正在为燕子移动端编写手机扩展。只允许输出 JSON，不要解释。\n" +
            "运行时使用 runtime=\"mobile-js\"，不要使用 C#、PowerShell、Windows 路径、WPF 或桌面 API。\n" +
            "可用能力通过 permissions 声明：desktop.message、clipboard.read、clipboard.write、screenshot、share.text。\n" +
            "脚本入口使用 async function run(context)，通过 context.mobile.sendToDesktop(text)、context.mobile.toast(text)、context.mobile.getSharedText() 调用宿主。";
    }

    private static String defaultMobileExtensionJson() {
        return "{\n" +
            "  \"id\": \"mobile-send-text\",\n" +
            "  \"name\": \"发送文本到电脑\",\n" +
            "  \"version\": \"0.1.0\",\n" +
            "  \"category\": \"手机效率\",\n" +
            "  \"description\": \"把手机输入内容发送到电脑。\",\n" +
            "  \"icon\": \"mdi:cellphone-arrow-down\",\n" +
            "  \"runtime\": \"mobile-js\",\n" +
            "  \"permissions\": [\"desktop.message\", \"share.text\"],\n" +
            "  \"script\": {\n" +
            "    \"source\": \"async function run(context) {\\n  const text = context.mobile.getSharedText() || '燕子';\\n  context.mobile.toast('正在发送到电脑');\\n  context.mobile.sendToDesktop(text);\\n}\"\n" +
            "  }\n" +
            "}";
    }

    private static void registerDevice(String baseUrl, String token, String deviceId, String displayName) throws Exception {
        JSONObject payload = new JSONObject()
            .put("deviceId", deviceId)
            .put("platform", "android")
            .put("displayName", displayName)
            .put("capabilities", new JSONObject()
                .put("shareText", true)
                .put("sendToDesktop", true)
                .put("floatingWheel", true)
                .put("mobileExtension", true)
                .put("accessibilityEnabled", MobileAccessibilityService.isEnabled()));
        postJson(baseUrl, "/v1/me/devices", payload, token);
    }

    private static String postMessage(String baseUrl, String token, String sourceDeviceId, String text) throws Exception {
        JSONObject payload = new JSONObject()
            .put("sourceDeviceId", sourceDeviceId)
            .put("targetPlatform", "desktop")
            .put("kind", "text")
            .put("title", "手机轮盘发来消息")
            .put("text", text)
            .put("payload", new JSONObject()
                .put("source", "android-floating-wheel")
                .put("sourceDeviceName", Build.MANUFACTURER + " " + Build.MODEL)
                .put("createdAt", System.currentTimeMillis()));
        return postJson(baseUrl, "/v1/me/mobile/messages", payload, token).optString("messageId", "unknown");
    }

    private static String postScreenshotWebDavMessage(String baseUrl, String token, String sourceDeviceId, String webDavPath, int bytes, int width, int height) throws Exception {
        JSONObject payload = new JSONObject()
            .put("sourceDeviceId", sourceDeviceId)
            .put("targetPlatform", "desktop")
            .put("kind", "screenshot")
            .put("title", "手机截图")
            .put("text", "手机截图：" + width + "x" + height)
            .put("payload", new JSONObject()
                .put("source", "android-floating-wheel")
                .put("sourceDeviceName", Build.MANUFACTURER + " " + Build.MODEL)
                .put("screenshotMime", "image/jpeg")
                .put("screenshotWidth", width)
                .put("screenshotHeight", height)
                .put("screenshotBytes", bytes)
                .put("webDavPath", webDavPath)
                .put("expiresAt", System.currentTimeMillis() + 30L * 24L * 60L * 60L * 1000L)
                .put("createdAt", System.currentTimeMillis()));
        return postJson(baseUrl, "/v1/me/mobile/messages", payload, token).optString("messageId", "unknown");
    }

    private static WebDavConfig fetchWebDavConfig(String baseUrl, String token) throws Exception {
        JSONObject json = getJson(baseUrl, "/v1/sync/webdav-config", token);
        WebDavConfig config = new WebDavConfig();
        config.serverUrl = json.optString("serverUrl", "https://dav.jianguoyun.com/dav/");
        config.rootPath = json.optString("rootPath", "/yanzi");
        config.username = json.optString("username", "");
        config.password = json.optString("password", "");
        if (!json.optBoolean("enabled", false) || config.username.trim().isEmpty() || config.password.trim().isEmpty()) {
            throw new IllegalStateException("账号未配置可用的坚果云 WebDAV。");
        }
        return config;
    }

    private static String uploadScreenshotToWebDav(WebDavConfig config, byte[] bytes) throws Exception {
        String day = new SimpleDateFormat("yyyyMMdd", Locale.ROOT).format(new Date());
        String fileName = "mobile-screenshot-" + day + "-" + UUID.randomUUID().toString().replace("-", "") + ".jpg";
        cleanupExpiredWebDavTempFiles(config);
        String path = fileName;
        putWebDavBytes(config, path, bytes, "image/jpeg");
        upsertWebDavTempIndex(config, path, bytes.length);
        return path;
    }

    private static void cleanupExpiredWebDavTempFiles(WebDavConfig config) {
        try {
            JSONObject index = readWebDavJson(config, "mobile-screenshots-index.json");
            long now = System.currentTimeMillis();
            org.json.JSONArray items = index.optJSONArray("items");
            org.json.JSONArray kept = new org.json.JSONArray();
            if (items != null) {
                for (int i = 0; i < items.length(); i++) {
                    JSONObject item = items.optJSONObject(i);
                    if (item == null) {
                        continue;
                    }
                    String path = item.optString("path", "");
                    long expiresAt = item.optLong("expiresAt", 0);
                    if (expiresAt > 0 && expiresAt < now) {
                        deleteWebDav(config, path);
                    } else {
                        kept.put(item);
                    }
                }
            }
            index.put("items", kept);
            putWebDavBytes(config, "mobile-screenshots-index.json", index.toString().getBytes(StandardCharsets.UTF_8), "application/json");
        } catch (Exception ignored) {
        }
    }

    private static void upsertWebDavTempIndex(WebDavConfig config, String path, int bytes) {
        try {
            JSONObject index = readWebDavJson(config, "mobile-screenshots-index.json");
            org.json.JSONArray items = index.optJSONArray("items");
            if (items == null) {
                items = new org.json.JSONArray();
            }
            items.put(new JSONObject()
                .put("path", path)
                .put("bytes", bytes)
                .put("createdAt", System.currentTimeMillis())
                .put("expiresAt", System.currentTimeMillis() + 30L * 24L * 60L * 60L * 1000L));
            index.put("items", items);
            putWebDavBytes(config, "mobile-screenshots-index.json", index.toString().getBytes(StandardCharsets.UTF_8), "application/json");
        } catch (Exception ignored) {
        }
    }

    private static JSONObject readWebDavJson(WebDavConfig config, String path) {
        try {
            String text = new String(getWebDavBytes(config, path), StandardCharsets.UTF_8);
            return new JSONObject(text);
        } catch (Exception ignored) {
            return new JSONObject();
        }
    }

    private static JSONObject postJson(String baseUrl, String path, JSONObject payload, String token) throws Exception {
        URL url = new URL(baseUrl + path);
        HttpURLConnection connection = (HttpURLConnection) url.openConnection();
        connection.setRequestMethod("POST");
        connection.setConnectTimeout(15000);
        connection.setReadTimeout(15000);
        connection.setDoOutput(true);
        connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
        if (token != null && !token.trim().isEmpty()) {
            connection.setRequestProperty("Authorization", "Bearer " + token);
        }
        try (OutputStreamWriter writer = new OutputStreamWriter(connection.getOutputStream(), StandardCharsets.UTF_8)) {
            writer.write(payload.toString());
        }
        int status = connection.getResponseCode();
        String body = readBody(status >= 400 ? connection.getErrorStream() : connection.getInputStream());
        if (status < 200 || status >= 300) {
            throw new IllegalStateException("HTTP " + status + ": " + body);
        }
        return body.trim().isEmpty() ? new JSONObject() : new JSONObject(body);
    }

    private static JSONObject getJson(String baseUrl, String path, String token) throws Exception {
        URL url = new URL(baseUrl + path);
        HttpURLConnection connection = (HttpURLConnection) url.openConnection();
        connection.setRequestMethod("GET");
        connection.setConnectTimeout(15000);
        connection.setReadTimeout(15000);
        connection.setRequestProperty("Accept", "application/json");
        if (token != null && !token.trim().isEmpty()) {
            connection.setRequestProperty("Authorization", "Bearer " + token);
        }
        int status = connection.getResponseCode();
        String body = readBody(status >= 400 ? connection.getErrorStream() : connection.getInputStream());
        if (status < 200 || status >= 300) {
            throw new IllegalStateException("HTTP " + status + ": " + body);
        }
        return body.trim().isEmpty() ? new JSONObject() : new JSONObject(body);
    }

    private static void ensureWebDavCollection(WebDavConfig config, String path) throws Exception {
        HttpURLConnection connection = openWebDav(config, path);
        connection.setRequestMethod("MKCOL");
        int status = connection.getResponseCode();
        readBody(status >= 400 ? connection.getErrorStream() : connection.getInputStream());
        if (status >= 200 && status < 300 || status == 405) {
            return;
        }
        if (status == 409 && path.contains("/")) {
            String parent = path.substring(0, path.lastIndexOf('/'));
            ensureWebDavCollection(config, parent);
            ensureWebDavCollection(config, path);
            return;
        }
        throw new IllegalStateException("WebDAV MKCOL failed " + status + ": " + path);
    }

    private static void putWebDavBytes(WebDavConfig config, String path, byte[] bytes, String contentType) throws Exception {
        HttpURLConnection connection = openWebDav(config, path);
        connection.setRequestMethod("PUT");
        connection.setConnectTimeout(20000);
        connection.setReadTimeout(30000);
        connection.setDoOutput(true);
        connection.setRequestProperty("Content-Type", contentType);
        connection.setRequestProperty("Content-Length", String.valueOf(bytes.length));
        connection.getOutputStream().write(bytes);
        int status = connection.getResponseCode();
        String body = readBody(status >= 400 ? connection.getErrorStream() : connection.getInputStream());
        if (status < 200 || status >= 300) {
            throw new IllegalStateException("WebDAV PUT failed " + status + ": " + body);
        }
    }

    private static byte[] getWebDavBytes(WebDavConfig config, String path) throws Exception {
        HttpURLConnection connection = openWebDav(config, path);
        connection.setRequestMethod("GET");
        int status = connection.getResponseCode();
        if (status < 200 || status >= 300) {
            throw new IllegalStateException("WebDAV GET failed " + status);
        }
        InputStream stream = connection.getInputStream();
        java.io.ByteArrayOutputStream buffer = new java.io.ByteArrayOutputStream();
        byte[] data = new byte[8192];
        int read;
        while ((read = stream.read(data)) >= 0) {
            buffer.write(data, 0, read);
        }
        return buffer.toByteArray();
    }

    private static void deleteWebDav(WebDavConfig config, String path) {
        try {
            HttpURLConnection connection = openWebDav(config, path);
            connection.setRequestMethod("DELETE");
            connection.getResponseCode();
        } catch (Exception ignored) {
        }
    }

    private static HttpURLConnection openWebDav(WebDavConfig config, String path) throws Exception {
        URL url = new URL(buildWebDavUrl(config, path));
        HttpURLConnection connection = (HttpURLConnection) url.openConnection();
        String auth = Base64.getEncoder().encodeToString((config.username + ":" + config.password).getBytes(StandardCharsets.UTF_8));
        connection.setRequestProperty("Authorization", "Basic " + auth);
        connection.setRequestProperty("Accept", "*/*");
        return connection;
    }

    private static String buildWebDavUrl(WebDavConfig config, String path) throws Exception {
        String base = config.serverUrl == null || config.serverUrl.trim().isEmpty()
            ? "https://dav.jianguoyun.com/dav/"
            : config.serverUrl.trim();
        while (base.endsWith("/")) {
            base = base.substring(0, base.length() - 1);
        }
        String root = config.rootPath == null || config.rootPath.trim().isEmpty() ? "yanzi" : config.rootPath.trim();
        root = trimSlashes(root);
        String relative = trimSlashes(path);
        String full = root.isEmpty() ? relative : (relative.isEmpty() ? root : root + "/" + relative);
        String[] parts = full.split("/");
        StringBuilder encoded = new StringBuilder(base);
        for (String part : parts) {
            if (!part.isEmpty()) {
                encoded.append('/').append(java.net.URLEncoder.encode(part, "UTF-8").replace("+", "%20"));
            }
        }
        return encoded.toString();
    }

    private static String trimSlashes(String value) {
        String result = value == null ? "" : value.trim();
        while (result.startsWith("/")) {
            result = result.substring(1);
        }
        while (result.endsWith("/")) {
            result = result.substring(0, result.length() - 1);
        }
        return result;
    }

    private static String readBody(InputStream stream) throws Exception {
        if (stream == null) {
            return "";
        }
        StringBuilder builder = new StringBuilder();
        try (BufferedReader reader = new BufferedReader(new InputStreamReader(stream, StandardCharsets.UTF_8))) {
            String line;
            while ((line = reader.readLine()) != null) {
                builder.append(line);
            }
        }
        return builder.toString();
    }

    private static final class WebDavConfig {
        String serverUrl;
        String rootPath;
        String username;
        String password;
    }
}
