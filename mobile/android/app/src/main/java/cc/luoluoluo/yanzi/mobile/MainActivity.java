package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.inputmethod.InputMethodManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebView;
import android.widget.Button;
import android.widget.EditText;
import android.widget.GridLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class MainActivity extends Activity {
    private static final String DEFAULT_BASE_URL = "https://sync.luoluoluo.cc.cd";

    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private SharedPreferences prefs;
    private String deviceId;
    private ScrollView mainScrollView;

    private EditText baseUrlInput;
    private EditText emailInput;
    private EditText passwordInput;
    private EditText textInput;
    private TextView statusText;
    private EditText mobileExtensionInput;
    private TextView mobileExtensionSectionTitle;
    private LinearLayout extensionList;
    private LinearLayout yanmList;
    private WebView activeYanmPreview;
    private LinearLayout activeYanmPreviewHost;
    private WebView activeMobileScriptRunner;
    private final android.os.Handler yanmSyncHandler = new android.os.Handler(android.os.Looper.getMainLooper());
    private final android.os.Handler diagnosticRefreshHandler = new android.os.Handler(android.os.Looper.getMainLooper());
    private final Runnable diagnosticRefreshRunnable = new Runnable() {
        @Override
        public void run() {
            refreshDiagnosticLogFromStore();
            diagnosticRefreshHandler.postDelayed(this, 1000);
        }
    };
    private JSONObject currentYanmState;
    private JSONObject currentYanmSnapshot;
    private Runnable pendingYanmSync;
    private final StringBuilder diagnosticLog = new StringBuilder();

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        prefs = getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE);
        deviceId = getOrCreateDeviceId();
        buildUi(extractSharedText(getIntent()));
        handleExternalAction(getIntent());
    }

    @Override
    protected void onResume() {
        super.onResume();
        refreshDiagnosticLogFromStore();
        diagnosticRefreshHandler.removeCallbacks(diagnosticRefreshRunnable);
        diagnosticRefreshHandler.postDelayed(diagnosticRefreshRunnable, 1000);
    }

    @Override
    protected void onPause() {
        diagnosticRefreshHandler.removeCallbacks(diagnosticRefreshRunnable);
        super.onPause();
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        String text = extractSharedText(intent);
        if (text != null && !text.trim().isEmpty()) {
            textInput.setText(text);
            setStatus("已接收系统分享内容，确认后可发送到电脑。");
        }
        handleExternalAction(intent);
    }

    private void handleExternalAction(Intent intent) {
        if (intent == null || intent.getAction() == null) {
            return;
        }

        String action = intent.getAction();
        if (action.endsWith(".extensions")) {
            setStatus("已从悬浮轮盘进入远程扩展。点击扩展图标会让电脑端执行。");
            refreshExtensions();
            scrollToView(extensionList);
        } else if (action.endsWith(".create-mobile-extension")) {
            openMobileExtensionEditor("添加手机扩展：可粘贴 AI 生成的 mobile-js JSON，保存后运行。");
        } else if (action.endsWith(".run-mobile-extension")) {
            openMobileExtensionEditor("运行手机扩展：确认 JSON 后点击“运行手机脚本”。");
        } else if (action.endsWith(".compose-text")) {
            focusTextComposer("从悬浮轮盘进入文本发送。输入内容后点击“发送到电脑”。");
        } else if (action.endsWith(".yanm")) {
            setStatus("已从悬浮轮盘进入手机燕幕。");
            refreshYanm();
            scrollToView(yanmList);
        } else if (action.endsWith(".refresh")) {
            setStatus("正在刷新移动端数据...");
            refreshExtensions();
            refreshYanm();
        }
    }

    private void buildUi(String sharedText) {
        ScrollView scrollView = new ScrollView(this);
        mainScrollView = scrollView;
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(20), dp(24), dp(20), dp(24));
        root.setBackgroundColor(Color.rgb(6, 17, 31));
        scrollView.addView(root);

        TextView title = textView("燕子移动端 MVP", 28, Color.WHITE, true);
        root.addView(title);
        root.addView(textView("把手机文本、链接和系统分享内容发送到同账号下的 Windows 燕子客户端。", 14, Color.rgb(182, 194, 214), false));

        baseUrlInput = input("云端地址", prefs.getString("baseUrl", DEFAULT_BASE_URL));
        emailInput = input("邮箱", prefs.getString("email", ""));
        passwordInput = input("密码", prefs.getString("password", ""));
        passwordInput.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        textInput = multiInput("发送给电脑的文本 / 链接", sharedText == null ? "" : sharedText);
        statusText = textView("", 14, Color.rgb(147, 197, 253), false);
        statusText.setTextIsSelectable(true);
        statusText.setMinLines(3);

        root.addView(baseUrlInput);
        root.addView(emailInput);
        root.addView(passwordInput);

        LinearLayout buttons = new LinearLayout(this);
        buttons.setOrientation(LinearLayout.HORIZONTAL);
        buttons.setGravity(Gravity.CENTER_VERTICAL);
        Button loginButton = button("登录并注册设备");
        Button logoutButton = button("退出");
        buttons.addView(loginButton, new LinearLayout.LayoutParams(0, dp(44), 1));
        buttons.addView(logoutButton, new LinearLayout.LayoutParams(0, dp(44), 1));
        root.addView(buttons);

        root.addView(textInput);
        Button sendButton = button("发送到电脑");
        root.addView(sendButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(48)));

        root.addView(sectionTitle("全局轮盘"));
        LinearLayout wheelButtons = new LinearLayout(this);
        wheelButtons.setOrientation(LinearLayout.VERTICAL);
        Button overlayButton = button("开启悬浮轮盘");
        Button accessibilityButton = button("开启无障碍能力");
        Button saveMobileScriptButton = button("保存手机扩展");
        Button mobilePromptButton = button("复制手机扩展提示词");
        Button runMobileScriptButton = button("运行手机脚本");
        wheelButtons.addView(overlayButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(44)));
        wheelButtons.addView(accessibilityButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(44)));
        wheelButtons.addView(saveMobileScriptButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(44)));
        wheelButtons.addView(mobilePromptButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(44)));
        wheelButtons.addView(runMobileScriptButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(44)));
        root.addView(wheelButtons);
        mobileExtensionSectionTitle = sectionTitle("手机扩展编辑器");
        root.addView(mobileExtensionSectionTitle);
        mobileExtensionInput = multiInput("手机扩展 JSON / mobile-js", prefs.getString("mobileExtensionDraft", defaultMobileExtensionJson()));
        root.addView(mobileExtensionInput);

        root.addView(sectionTitle("远程扩展"));
        Button refreshExtensionsButton = button("刷新扩展列表");
        root.addView(refreshExtensionsButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(44)));
        extensionList = new LinearLayout(this);
        extensionList.setOrientation(LinearLayout.VERTICAL);
        root.addView(extensionList);

        root.addView(sectionTitle("手机燕幕"));
        Button refreshYanmButton = button("刷新燕幕");
        root.addView(refreshYanmButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(44)));
        yanmList = new LinearLayout(this);
        yanmList.setOrientation(LinearLayout.VERTICAL);
        root.addView(yanmList);

        root.addView(statusText);

        LinearLayout logButtons = new LinearLayout(this);
        logButtons.setOrientation(LinearLayout.HORIZONTAL);
        Button copyLogButton = button("复制日志");
        Button clearLogButton = button("清空日志");
        logButtons.addView(copyLogButton, new LinearLayout.LayoutParams(0, dp(44), 1));
        logButtons.addView(clearLogButton, new LinearLayout.LayoutParams(0, dp(44), 1));
        root.addView(logButtons);
        root.addView(textView("设备 ID：" + deviceId, 11, Color.rgb(100, 116, 139), false));

        loginButton.setOnClickListener(v -> loginAndRegister());
        logoutButton.setOnClickListener(v -> {
            prefs.edit().putString("token", "").apply();
            setStatus("已清除本地登录态。");
        });
        sendButton.setOnClickListener(v -> sendToDesktop());
        overlayButton.setOnClickListener(v -> startFloatingWheel());
        accessibilityButton.setOnClickListener(v -> openAccessibilitySettings());
        saveMobileScriptButton.setOnClickListener(v -> saveMobileExtensionDraft());
        mobilePromptButton.setOnClickListener(v -> copyMobileExtensionPrompt());
        runMobileScriptButton.setOnClickListener(v -> runMobileScript());
        refreshExtensionsButton.setOnClickListener(v -> refreshExtensions());
        refreshYanmButton.setOnClickListener(v -> refreshYanm());
        copyLogButton.setOnClickListener(v -> copyDiagnostics());
        clearLogButton.setOnClickListener(v -> {
            diagnosticLog.setLength(0);
            MobileDiagnostics.clear(this);
            statusText.setText("");
            setStatus("日志已清空。");
        });

        setContentView(scrollView);
        setStatus(prefs.getString("token", "").trim().isEmpty() ? "请先登录燕子账号。" : "已加载本地登录态。");
        if (!prefs.getString("token", "").trim().isEmpty()) {
            refreshExtensions();
            refreshYanm();
        }
    }

    private void focusTextComposer(String status) {
        setStatus(status);
        textInput.requestFocus();
        scrollToView(textInput);
        showKeyboard(textInput);
    }

    private void openMobileExtensionEditor(String status) {
        setStatus(status);
        mobileExtensionInput.requestFocus();
        scrollToView(mobileExtensionSectionTitle);
        showKeyboard(mobileExtensionInput);
    }

    private void scrollToView(View view) {
        if (mainScrollView == null || view == null) {
            return;
        }

        mainScrollView.post(() -> mainScrollView.smoothScrollTo(0, Math.max(0, view.getTop() - dp(16))));
    }

    private void showKeyboard(View view) {
        view.postDelayed(() -> {
            InputMethodManager manager = (InputMethodManager) getSystemService(Context.INPUT_METHOD_SERVICE);
            if (manager != null) {
                manager.showSoftInput(view, InputMethodManager.SHOW_IMPLICIT);
            }
        }, 250);
    }

    private void startFloatingWheel() {
        if (!Settings.canDrawOverlays(this)) {
            Intent intent = new Intent(
                Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                Uri.parse("package:" + getPackageName()));
            startActivity(intent);
            setStatus("请开启“允许显示在其他应用上层”，返回后再次点击开启悬浮轮盘。");
            return;
        }

        Intent intent = new Intent(this, FloatingWheelService.class);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startService(intent);
        } else {
            startService(intent);
        }
        setStatus("悬浮轮盘已启动。点击屏幕上的“燕”按钮打开手机轮盘。");
    }

    private void openAccessibilitySettings() {
        Intent intent = new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS);
        startActivity(intent);
        setStatus("请在无障碍设置中开启“燕子移动端”，用于截图和后续全局手势能力。");
    }

    private void copyMobileExtensionPrompt() {
        String prompt =
            "你正在为燕子移动端编写手机扩展。只允许输出 JSON，不要解释。\\n" +
            "运行时使用 runtime=\\\"mobile-js\\\"，不要使用 C#、PowerShell、Windows 路径、WPF 或桌面 API。\\n" +
            "可用能力通过 permissions 声明：desktop.message、clipboard.read、clipboard.write、screenshot、share.text。\\n" +
            "脚本入口使用 async function run(context)，通过 context.mobile.sendToDesktop(text)、context.mobile.toast(text)、context.mobile.getSharedText() 调用宿主。\\n" +
            "输出字段至少包含 id、name、version、category、description、icon、runtime、permissions、script.source。";
        ClipboardManager manager = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
        manager.setPrimaryClip(ClipData.newPlainText("Yanzi mobile extension prompt", prompt));
        setStatus("已复制手机端扩展提示词。");
    }

    private void saveMobileExtensionDraft() {
        try {
            String draft = mobileExtensionInput.getText().toString();
            String id = "mobile-extension-draft";
            String name = "手机扩展草稿";
            if (draft.trim().startsWith("{")) {
                JSONObject json = new JSONObject(draft);
                id = firstNonEmpty(json.optString("id"), id);
                name = firstNonEmpty(json.optString("name"), json.optString("displayName"), name);
            }
            prefs.edit()
                .putString("mobileExtensionDraft", draft)
                .putString("mobileExtensionDraftId", id)
                .putString("mobileExtensionDraftName", name)
                .apply();
            setStatus("手机扩展已保存：" + name + "。可从悬浮轮盘“运行”进入执行。");
        } catch (Exception ex) {
            setStatus("手机扩展保存失败：" + ex.getMessage());
        }
    }

    private void runMobileScript() {
        try {
            String draft = mobileExtensionInput.getText().toString();
            prefs.edit().putString("mobileExtensionDraft", draft).apply();
            String source = extractMobileScriptSource(draft);
            if (source.trim().isEmpty()) {
                throw new IllegalStateException("脚本为空。");
            }

            WebView runner = new WebView(this);
            activeMobileScriptRunner = runner;
            runner.getSettings().setJavaScriptEnabled(true);
            runner.addJavascriptInterface(new MobileJsBridge(), "yanziMobileJsHost");
            String html = "<!doctype html><html><body><script>" +
                "window.context={mobile:{" +
                "toast:function(text){yanziMobileJsHost.toast(String(text||''));}," +
                "sendToDesktop:function(text){yanziMobileJsHost.sendToDesktop(String(text||''));}," +
                "getSharedText:function(){return yanziMobileJsHost.getSharedText();}" +
                "}};" +
                "async function __run(){try{" + source + "\n;if(typeof run==='function'){await run(window.context);}yanziMobileJsHost.done('脚本执行完成');}" +
                "catch(e){yanziMobileJsHost.fail(String(e&&e.message?e.message:e));}}" +
                "__run();" +
                "</script></body></html>";
            runner.loadDataWithBaseURL(null, html, "text/html", "UTF-8", null);
            setStatus("手机脚本已启动。");
        } catch (Exception ex) {
            setStatus("手机脚本启动失败：" + ex.getMessage());
        }
    }

    private static String extractMobileScriptSource(String draft) throws Exception {
        String text = draft == null ? "" : draft.trim();
        if (text.startsWith("{")) {
            JSONObject json = new JSONObject(text);
            JSONObject script = json.optJSONObject("script");
            if (script != null) {
                return script.optString("source", "");
            }
        }
        return text;
    }

    private String defaultMobileExtensionJson() {
        return "{\n" +
            "  \"id\": \"mobile-send-selection\",\n" +
            "  \"name\": \"发送输入到电脑\",\n" +
            "  \"version\": \"0.1.0\",\n" +
            "  \"category\": \"手机效率\",\n" +
            "  \"description\": \"把手机输入框内容发送到电脑。\",\n" +
            "  \"icon\": \"mdi:cellphone-arrow-down\",\n" +
            "  \"runtime\": \"mobile-js\",\n" +
            "  \"permissions\": [\"desktop.message\", \"share.text\"],\n" +
            "  \"script\": {\n" +
            "    \"source\": \"async function run(context) {\\n  const text = context.mobile.getSharedText() || '来自手机脚本的消息';\\n  context.mobile.toast('正在发送到电脑');\\n  context.mobile.sendToDesktop(text);\\n}\"\n" +
            "  }\n" +
            "}";
    }

    private void loginAndRegister() {
        setStatus("正在登录...");
        executor.execute(() -> {
            String baseUrl = normalizedBaseUrl();
            String email = emailInput.getText().toString().trim();
            String token;
            try {
                token = YanziApiClient.login(baseUrl, email, passwordInput.getText().toString());
            } catch (Exception ex) {
                runOnUiThread(() -> setStatus("登录失败：" + ex.getMessage()));
                return;
            }

            runOnUiThread(() -> setStatus("登录成功，正在注册手机设备..."));
            try {
                YanziApiClient.registerDevice(baseUrl, token, deviceId, buildDeviceName());
                prefs.edit()
                    .putString("baseUrl", baseUrl)
                    .putString("email", email)
                    .putString("password", passwordInput.getText().toString())
                    .putString("token", token)
                    .apply();
                runOnUiThread(() -> {
                    setStatus("登录成功，设备已注册。");
                    refreshExtensions();
                    refreshYanm();
                });
            } catch (Exception ex) {
                prefs.edit()
                    .putString("baseUrl", baseUrl)
                    .putString("email", email)
                    .putString("password", passwordInput.getText().toString())
                    .putString("token", token)
                    .apply();
                runOnUiThread(() -> setStatus("登录成功，但设备注册失败：" + ex.getMessage()));
            }
        });
    }

    private void sendToDesktop() {
        sendTextValueToDesktop(textInput.getText().toString(), "正在发送到电脑...");
    }

    private void sendTextValueToDesktop(String text, String pendingStatus) {
        setStatus(pendingStatus);
        executor.execute(() -> {
            try {
                String baseUrl = normalizedBaseUrl();
                String token = requireToken();
                String messageId;
                try {
                    YanziApiClient.registerDevice(baseUrl, token, deviceId, buildDeviceName());
                    messageId = YanziApiClient.sendTextToDesktop(baseUrl, token, deviceId, text);
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = refreshToken();
                    YanziApiClient.registerDevice(baseUrl, token, deviceId, buildDeviceName());
                    messageId = YanziApiClient.sendTextToDesktop(baseUrl, token, deviceId, text);
                }
                String sentMessageId = messageId;
                runOnUiThread(() -> setStatus("已发送到云端，messageId=" + sentMessageId + "。电脑端在线时会在 5 秒内收到。"));
            } catch (Exception ex) {
                runOnUiThread(() -> setStatus("发送失败：" + ex.getMessage()));
            }
        });
    }

    private void refreshExtensions() {
        extensionList.removeAllViews();
        extensionList.addView(textView("正在读取账号扩展...", 13, Color.rgb(148, 163, 184), false));
        executor.execute(() -> {
            try {
                String baseUrl = normalizedBaseUrl();
                String token = requireToken();
                List<RemoteExtension> extensions;
                try {
                    extensions = YanziApiClient.fetchRunnableExtensions(baseUrl, token);
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = refreshToken();
                    extensions = YanziApiClient.fetchRunnableExtensions(baseUrl, token);
                }
                List<RemoteExtension> loadedExtensions = extensions;
                runOnUiThread(() -> renderExtensions(loadedExtensions));
            } catch (Exception ex) {
                runOnUiThread(() -> {
                    extensionList.removeAllViews();
                    extensionList.addView(textView("扩展列表读取失败。", 13, Color.rgb(248, 113, 113), false));
                    setStatus("扩展列表读取失败：" + ex.getMessage());
                });
            }
        });
    }

    private void renderExtensions(List<RemoteExtension> extensions) {
        extensionList.removeAllViews();
        if (extensions.isEmpty()) {
            extensionList.addView(textView("暂无可远程执行扩展。请先在电脑端发布/同步扩展。", 13, Color.rgb(148, 163, 184), false));
            return;
        }

        GridLayout grid = new GridLayout(this);
        grid.setColumnCount(4);
        extensionList.addView(grid);

        int screenWidth = getResources().getDisplayMetrics().widthPixels;
        int cellWidth = Math.max(dp(72), (screenWidth - dp(56)) / 4);
        for (RemoteExtension extension : extensions) {
            LinearLayout card = iconCard();
            card.setGravity(Gravity.CENTER_VERTICAL);
            card.setOnClickListener(v -> runRemoteExtension(extension));
            GridLayout.LayoutParams cardParams = new GridLayout.LayoutParams();
            cardParams.width = cellWidth;
            cardParams.height = GridLayout.LayoutParams.WRAP_CONTENT;
            cardParams.setMargins(dp(3), dp(6), dp(3), dp(6));
            card.setLayoutParams(cardParams);

            TextView icon = textView(extension.iconText(), 20, Color.WHITE, true);
            icon.setGravity(Gravity.CENTER);
            icon.setBackgroundColor(Color.rgb(21, 94, 117));
            LinearLayout.LayoutParams iconParams = new LinearLayout.LayoutParams(dp(54), dp(54));
            iconParams.setMargins(0, 0, 0, dp(6));
            card.addView(icon, iconParams);

            TextView name = textView(extension.name, 11, Color.WHITE, false);
            name.setGravity(Gravity.CENTER);
            name.setMaxLines(2);
            card.addView(name, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
            grid.addView(card);
        }
    }

    private void runRemoteExtension(RemoteExtension extension) {
        setStatus("正在发送扩展执行请求：" + extension.name);
        executor.execute(() -> {
            try {
                String baseUrl = normalizedBaseUrl();
                String token = requireToken();
                String messageId;
                try {
                    messageId = YanziApiClient.runExtensionOnDesktop(
                    baseUrl,
                    token,
                    deviceId,
                    buildDeviceName(),
                    extension.extensionId,
                    textInput.getText().toString());
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = refreshToken();
                    messageId = YanziApiClient.runExtensionOnDesktop(
                        baseUrl,
                        token,
                        deviceId,
                        buildDeviceName(),
                        extension.extensionId,
                        textInput.getText().toString());
                }
                String sentMessageId = messageId;
                runOnUiThread(() -> setStatus("扩展执行请求已发送，messageId=" + sentMessageId + "，扩展=" + extension.name));
            } catch (Exception ex) {
                runOnUiThread(() -> setStatus("扩展执行请求失败：" + ex.getMessage()));
            }
        });
    }

    private void refreshYanm() {
        yanmList.removeAllViews();
        yanmList.addView(textView("正在读取燕幕...", 13, Color.rgb(148, 163, 184), false));
        executor.execute(() -> {
            try {
                String baseUrl = normalizedBaseUrl();
                String token = requireToken();
                JSONObject yanm;
                try {
                    yanm = YanziApiClient.fetchYanmState(baseUrl, token);
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = refreshToken();
                    yanm = YanziApiClient.fetchYanmState(baseUrl, token);
                }
                JSONObject loadedYanm = yanm;
                runOnUiThread(() -> renderYanm(loadedYanm));
            } catch (Exception ex) {
                runOnUiThread(() -> {
                    yanmList.removeAllViews();
                    yanmList.addView(textView("燕幕读取失败。", 13, Color.rgb(248, 113, 113), false));
                    setStatus("燕幕读取失败：" + ex.getMessage());
                });
            }
        });
    }

    private void renderYanm(JSONObject yanm) {
        currentYanmSnapshot = yanm;
        currentYanmState = firstObject(yanm, "componentState", "ComponentState");
        if (currentYanmState == null) {
            currentYanmState = new JSONObject();
            try {
                currentYanmSnapshot.put("componentState", currentYanmState);
            } catch (Exception ignored) {
            }
        }
        yanmList.removeAllViews();
        JSONArray components = firstArray(yanm, "components", "Components");
        if (components == null || components.length() == 0) {
            yanmList.addView(textView("暂无燕幕组件。", 13, Color.rgb(148, 163, 184), false));
            return;
        }

        for (int i = 0; i < components.length(); i++) {
            JSONObject component = components.optJSONObject(i);
            if (component == null) {
                continue;
            }

            String title = firstNonEmpty(
                component.optString("title"),
                component.optString("Title"),
                component.optString("name"),
                component.optString("Name"),
                "组件 " + (i + 1));
            String type = firstNonEmpty(
                component.optString("type"),
                component.optString("Type"),
                component.optString("kind"),
                component.optString("Kind"),
                "component");
            LinearLayout card = card();
            card.addView(textView(title, 16, Color.WHITE, true));
            card.addView(textView(type, 11, Color.rgb(94, 234, 212), false));
            String html = firstNonEmpty(
                component.optString("html"),
                component.optString("Html"),
                component.optString("markup"),
                component.optString("Markup"),
                component.optString("contentHtml"),
                component.optString("ContentHtml"));
            if (!html.isEmpty()) {
                TextView hint = textView("点击预览组件界面", 12, Color.rgb(182, 194, 214), false);
                card.addView(hint);
                LinearLayout previewHost = new LinearLayout(this);
                previewHost.setOrientation(LinearLayout.VERTICAL);
                card.addView(previewHost);
                String htmlForPreview = html;
                String componentId = firstNonEmpty(component.optString("id"), component.optString("Id"), title);
                card.setOnClickListener(v -> toggleYanmPreview(previewHost, htmlForPreview, componentId, title));
            } else {
                String summary = summarizeYanmComponent(component);
                card.addView(textView(summary, 12, Color.rgb(182, 194, 214), false));
            }
            yanmList.addView(card);
        }

        setStatus("燕幕已加载：" + components.length() + " 个组件。");
    }

    private void toggleYanmPreview(LinearLayout previewHost, String html, String componentId, String componentTitle) {
        if (activeYanmPreviewHost == previewHost && activeYanmPreview != null) {
            previewHost.removeAllViews();
            activeYanmPreview.destroy();
            activeYanmPreview = null;
            activeYanmPreviewHost = null;
            return;
        }

        if (activeYanmPreviewHost != null) {
            activeYanmPreviewHost.removeAllViews();
        }
        if (activeYanmPreview != null) {
            activeYanmPreview.destroy();
        }

        WebView webView = new WebView(this);
        activeYanmPreview = webView;
        activeYanmPreviewHost = previewHost;
        webView.setBackgroundColor(Color.TRANSPARENT);
        webView.setVerticalScrollBarEnabled(false);
        webView.setHorizontalScrollBarEnabled(false);
        webView.getSettings().setJavaScriptEnabled(true);
        webView.getSettings().setDomStorageEnabled(true);
        webView.getSettings().setLoadWithOverviewMode(false);
        webView.getSettings().setUseWideViewPort(false);
        webView.getSettings().setTextZoom(145);
        webView.setInitialScale(145);
        webView.addJavascriptInterface(new YanmMobileBridge(componentId, componentTitle), "yanmMobileHost");
        webView.loadDataWithBaseURL(null, wrapYanmHtml(html, componentId, componentTitle), "text/html", "UTF-8", null);
        previewHost.addView(webView, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(420)));
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
            String baseUrl = normalizedBaseUrl();
            String email = prefs.getString("email", "");
            String password = prefs.getString("password", "");
            if (email == null || email.trim().isEmpty() || password == null || password.isEmpty()) {
                throw new IllegalStateException("请先登录。");
            }

            String token = YanziApiClient.login(baseUrl, email.trim(), password);
            prefs.edit().putString("baseUrl", baseUrl).putString("token", token).apply();
            return token;
        } catch (Exception ex) {
            throw new IllegalStateException("登录态已失效，请重新登录：" + ex.getMessage());
        }
    }

    private static boolean isUnauthorized(Exception ex) {
        String message = ex.getMessage();
        return message != null && (
            message.contains("401") ||
            message.toLowerCase(Locale.ROOT).contains("token expired") ||
            message.toLowerCase(Locale.ROOT).contains("unauthorized"));
    }

    private String normalizedBaseUrl() {
        String value = baseUrlInput.getText().toString().trim();
        if (value.trim().isEmpty()) {
            return DEFAULT_BASE_URL;
        }

        int v1Index = value.indexOf("/v1/");
        if (v1Index >= 0) {
            value = value.substring(0, v1Index);
        }
        if (value.endsWith("/health")) {
            value = value.substring(0, value.length() - "/health".length());
        }
        if (value.contains("yanzi.luoluoluo.cc.cd")) {
            value = DEFAULT_BASE_URL;
        }
        while (value.endsWith("/")) {
            value = value.substring(0, value.length() - 1);
        }
        return value.trim().isEmpty() ? DEFAULT_BASE_URL : value;
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
        return name.trim().isEmpty() ? "Android 手机" : name;
    }

    private void setStatus(String status) {
        diagnosticLog.setLength(0);
        diagnosticLog.append(MobileDiagnostics.append(this, status));
        statusText.setText(diagnosticLog.toString());
    }

    private void refreshDiagnosticLogFromStore() {
        if (statusText == null) {
            return;
        }

        String stored = MobileDiagnostics.get(this);
        if (!stored.equals(diagnosticLog.toString())) {
            diagnosticLog.setLength(0);
            diagnosticLog.append(stored);
            statusText.setText(stored);
        }
    }

    private void copyDiagnostics() {
        refreshDiagnosticLogFromStore();
        String value = diagnosticLog.length() == 0 ? statusText.getText().toString() : diagnosticLog.toString();
        ClipboardManager manager = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
        manager.setPrimaryClip(ClipData.newPlainText("Yanzi mobile diagnostics", value));
        Toast.makeText(this, "已复制日志", Toast.LENGTH_SHORT).show();
    }

    private void trimDiagnosticLog() {
        int maxLength = 6000;
        if (diagnosticLog.length() <= maxLength) {
            return;
        }

        diagnosticLog.delete(0, diagnosticLog.length() - maxLength);
    }

    private void scheduleYanmCloudSync(String reason) {
        if (pendingYanmSync != null) {
            yanmSyncHandler.removeCallbacks(pendingYanmSync);
        }

        pendingYanmSync = () -> syncYanmStateToCloud(reason);
        yanmSyncHandler.postDelayed(pendingYanmSync, 1000);
        setStatus("燕幕状态待同步到云端：" + reason);
    }

    private void syncYanmStateToCloud(String reason) {
        JSONObject snapshot = currentYanmSnapshot;
        if (snapshot == null) {
            setStatus("燕幕同步跳过：没有完整快照。");
            return;
        }

        executor.execute(() -> {
            try {
                String baseUrl = normalizedBaseUrl();
                String token = requireToken();
                try {
                    YanziApiClient.putYanmState(baseUrl, token, snapshot);
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = refreshToken();
                    YanziApiClient.putYanmState(baseUrl, token, snapshot);
                }
                runOnUiThread(() -> setStatus("燕幕状态已同步到云端：" + reason));
            } catch (Exception ex) {
                runOnUiThread(() -> setStatus("燕幕状态同步失败：" + ex.getMessage()));
            }
        });
    }

    private TextView textView(String text, int sp, int color, boolean bold) {
        TextView view = new TextView(this);
        view.setText(text);
        view.setTextColor(color);
        view.setTextSize(sp);
        view.setPadding(0, dp(6), 0, dp(6));
        if (bold) {
            view.setTypeface(view.getTypeface(), android.graphics.Typeface.BOLD);
        }
        return view;
    }

    private TextView sectionTitle(String text) {
        TextView view = textView(text, 18, Color.WHITE, true);
        view.setPadding(0, dp(18), 0, dp(8));
        return view;
    }

    private LinearLayout card() {
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setPadding(dp(14), dp(12), dp(14), dp(12));
        card.setBackgroundColor(Color.rgb(13, 31, 49));
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT,
            LinearLayout.LayoutParams.WRAP_CONTENT);
        params.setMargins(0, dp(8), 0, dp(8));
        card.setLayoutParams(params);
        return card;
    }

    private LinearLayout iconCard() {
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setGravity(Gravity.CENTER);
        card.setPadding(dp(6), dp(8), dp(6), dp(8));
        card.setBackgroundColor(Color.rgb(13, 31, 49));
        return card;
    }

    private EditText input(String hint, String value) {
        EditText input = new EditText(this);
        input.setHint(hint);
        input.setText(value == null ? "" : value);
        input.setSingleLine(true);
        input.setTextColor(Color.WHITE);
        input.setHintTextColor(Color.rgb(148, 163, 184));
        input.setPadding(dp(12), dp(10), dp(12), dp(10));
        return input;
    }

    private EditText multiInput(String hint, String value) {
        EditText input = input(hint, value);
        input.setSingleLine(false);
        input.setMinLines(5);
        input.setGravity(Gravity.TOP);
        return input;
    }

    private Button button(String text) {
        Button button = new Button(this);
        button.setText(text);
        return button;
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density + 0.5f);
    }

    private static String extractSharedText(Intent intent) {
        if (intent == null || !Intent.ACTION_SEND.equals(intent.getAction()) || !"text/plain".equals(intent.getType())) {
            return null;
        }
        return intent.getStringExtra(Intent.EXTRA_TEXT);
    }

    private static String firstNonEmpty(String... values) {
        for (String value : values) {
            if (value != null && !value.trim().isEmpty()) {
                return value.trim();
            }
        }
        return "";
    }

    private static JSONArray firstArray(JSONObject object, String... keys) {
        for (String key : keys) {
            JSONArray value = object.optJSONArray(key);
            if (value != null) {
                return value;
            }
        }
        return null;
    }

    private static JSONObject firstObject(JSONObject object, String... keys) {
        for (String key : keys) {
            JSONObject value = object.optJSONObject(key);
            if (value != null) {
                return value;
            }
        }
        return null;
    }

    private static String summarizeYanmComponent(JSONObject component) {
        String text = firstNonEmpty(
            component.optString("text"),
            component.optString("Text"),
            component.optString("content"),
            component.optString("Content"),
            component.optString("note"),
            component.optString("Note"),
            component.optString("description"),
            component.optString("Description"));
        if (text.isEmpty()) {
            text = component.toString();
        }
        text = text.replaceAll("\\s+", " ").trim();
        return text.length() > 140 ? text.substring(0, 140) + "..." : text;
    }

    private static String wrapYanmHtml(String html, String componentId, String componentTitle) {
        String trimmed = html == null ? "" : html.trim();
        String mobileHead = "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no\" />" +
            "<style id=\"yanm-mobile-adapter\">" +
            "html,body{margin:0!important;padding:0!important;background:#07111f!important;color:#fff;min-width:0!important;overflow:auto!important;}" +
            "body{font-size:18px!important;line-height:1.45!important;-webkit-text-size-adjust:145%;text-size-adjust:145%;}" +
            "*{box-sizing:border-box;max-width:100%!important;}" +
            "button,input,textarea,select{font-size:16px!important;}" +
            "img,svg,canvas,video{max-width:100%!important;height:auto;}" +
            "</style>";
        String bridge = "<script>(function(){var componentId=" + JSONObject.quote(componentId) + ";var componentTitle=" + JSONObject.quote(componentTitle) + ";" +
            "window.yanm=window.yanm||{};window.yanm.componentId=componentId;window.yanm.componentTitle=componentTitle;" +
            "window.yanmHost=window.yanmHost||{};" +
            "function emit(d){try{window.dispatchEvent(new CustomEvent('yanm:message',{detail:d||{}}));}catch(e){}}" +
            "window.yanmHost.getState=function(key){key=String(key||'');var value=String(yanmMobileHost.getState(key)||'');var res={key:key,value:value};emit({type:'host.state',key:key,value:value});return res;};" +
            "window.yanmHost.setState=function(key,value){key=String(key||'');value=String(value||'');yanmMobileHost.setState(key,value);emit({type:'host.state',key:key,value:value});return {key:key,value:value};};" +
            "window.yanmHost.requestSystemInfo=function(){var data=JSON.parse(yanmMobileHost.getSystemInfo());data.type='host.systemInfo';emit(data);return data;};" +
            "window.yanm.invoke=function(method,args){args=args||{};if(method==='state.get')return Promise.resolve(window.yanmHost.getState(args.key));if(method==='state.set')return Promise.resolve(window.yanmHost.setState(args.key,args.value));if(method==='system.info')return Promise.resolve(window.yanmHost.requestSystemInfo());return Promise.reject(new Error('unsupported mobile method '+method));};" +
            "window.dispatchEvent(new CustomEvent('yanm:message',{detail:{type:'host.ready',componentId:componentId}}));})();</script>";
        if (trimmed.toLowerCase(Locale.ROOT).contains("<html")) {
            String lower = trimmed.toLowerCase(Locale.ROOT);
            int headEnd = lower.indexOf("</head>");
            String withHead = headEnd >= 0
                ? trimmed.substring(0, headEnd) + mobileHead + trimmed.substring(headEnd)
                : trimmed.replaceFirst("(?i)<html[^>]*>", "$0<head>" + mobileHead + "</head>");
            String lowerWithHead = withHead.toLowerCase(Locale.ROOT);
            int bodyEnd = lowerWithHead.lastIndexOf("</body>");
            return bodyEnd >= 0 ? withHead.substring(0, bodyEnd) + bridge + withHead.substring(bodyEnd) : withHead + bridge;
        }

        return "<!doctype html><html><head>" + mobileHead +
            "</head><body>" + trimmed + bridge + "</body></html>";
    }

    private final class YanmMobileBridge {
        private final String componentId;
        private final String componentTitle;

        YanmMobileBridge(String componentId, String componentTitle) {
            this.componentId = componentId;
            this.componentTitle = componentTitle;
        }

        @JavascriptInterface
        public String getState(String key) {
            JSONObject state = currentYanmState == null ? new JSONObject() : currentYanmState;
            return state.optString(key, "");
        }

        @JavascriptInterface
        public void setState(String key, String value) {
            try {
                if (currentYanmState == null) {
                    currentYanmState = new JSONObject();
                }
                currentYanmState.put(key, value);
                if (currentYanmSnapshot == null) {
                    currentYanmSnapshot = new JSONObject();
                }
                currentYanmSnapshot.put("componentState", currentYanmState);
                runOnUiThread(() -> {
                    setStatus("燕幕状态已在手机端更新：" + componentTitle + " / " + key);
                    scheduleYanmCloudSync(componentTitle + " / " + key);
                });
            } catch (Exception ignored) {
            }
        }

        @JavascriptInterface
        public String getSystemInfo() {
            try {
                return new JSONObject()
                    .put("machineName", Build.MANUFACTURER + " " + Build.MODEL)
                    .put("osVersion", "Android " + Build.VERSION.RELEASE)
                    .put("isNetworkAvailable", true)
                    .put("time", new SimpleDateFormat("HH:mm", Locale.getDefault()).format(new Date()))
                    .put("componentId", componentId)
                    .toString();
            } catch (Exception ex) {
                return "{}";
            }
        }
    }

    private final class MobileJsBridge {
        @JavascriptInterface
        public void toast(String text) {
            runOnUiThread(() -> Toast.makeText(MainActivity.this, text, Toast.LENGTH_SHORT).show());
        }

        @JavascriptInterface
        public void sendToDesktop(String text) {
            runOnUiThread(() -> sendTextValueToDesktop(text, "手机脚本正在发送到电脑..."));
        }

        @JavascriptInterface
        public String getSharedText() {
            return textInput == null ? "" : textInput.getText().toString();
        }

        @JavascriptInterface
        public void done(String text) {
            runOnUiThread(() -> setStatus(text));
        }

        @JavascriptInterface
        public void fail(String text) {
            runOnUiThread(() -> setStatus("手机脚本执行失败：" + text));
        }
    }

    private static final class RemoteExtension {
        final String extensionId;
        final String name;
        final String description;
        final String icon;

        RemoteExtension(String extensionId, String name, String description, String icon) {
            this.extensionId = extensionId;
            this.name = name;
            this.description = description;
            this.icon = icon == null ? "" : icon;
        }

        String iconText() {
            String value = icon.trim();
            if (value.startsWith("mdi:")) {
                String namePart = value.substring(4).replace("-", " ").trim();
                return namePart.isEmpty() ? "燕" : namePart.substring(0, 1).toUpperCase(Locale.ROOT);
            }

            String base = name.trim().isEmpty() ? extensionId : name.trim();
            return base.isEmpty() ? "燕" : base.substring(0, 1).toUpperCase(Locale.ROOT);
        }
    }

    private static final class YanziApiClient {
        static String login(String baseUrl, String email, String password) throws Exception {
            JSONObject payload = new JSONObject()
                .put("email", email)
                .put("password", password);
            return postJson(baseUrl, "/v1/auth/login", payload, null, "登录").getString("accessToken");
        }

        static void registerDevice(String baseUrl, String token, String deviceId, String displayName) throws Exception {
            JSONObject capabilities = new JSONObject()
                .put("shareText", true)
                .put("sendToDesktop", true);
            JSONObject payload = new JSONObject()
                .put("deviceId", deviceId)
                .put("platform", "android")
                .put("displayName", displayName)
                .put("capabilities", capabilities);
            postJson(baseUrl, "/v1/me/devices", payload, token, "设备注册");
        }

        static String sendTextToDesktop(String baseUrl, String token, String sourceDeviceId, String text) throws Exception {
            JSONObject payload = new JSONObject()
                .put("sourceDeviceId", sourceDeviceId)
                .put("targetPlatform", "desktop")
                .put("kind", "text")
                .put("title", "手机发来消息")
                .put("text", text)
                .put("payload", new JSONObject()
                    .put("source", "android")
                    .put("sourceDeviceName", Build.MANUFACTURER + " " + Build.MODEL)
                    .put("createdAt", System.currentTimeMillis()));
            return postJson(baseUrl, "/v1/me/mobile/messages", payload, token, "发送消息").optString("messageId", "unknown");
        }

        static String runExtensionOnDesktop(String baseUrl, String token, String sourceDeviceId, String sourceDeviceName, String extensionId, String inputText) throws Exception {
            JSONObject payload = new JSONObject()
                .put("sourceDeviceId", sourceDeviceId)
                .put("targetPlatform", "desktop")
                .put("kind", "run-extension")
                .put("title", "手机请求执行扩展")
                .put("text", inputText == null ? "" : inputText)
                .put("payload", new JSONObject()
                    .put("source", "android")
                    .put("sourceDeviceName", sourceDeviceName)
                    .put("extensionId", extensionId)
                    .put("createdAt", System.currentTimeMillis()));
            return postJson(baseUrl, "/v1/me/mobile/messages", payload, token, "执行扩展").optString("messageId", "unknown");
        }

        static List<RemoteExtension> fetchRunnableExtensions(String baseUrl, String token) throws Exception {
            JSONObject payload = getJson(baseUrl, "/v1/me/extensions", token, "读取扩展列表");
            JSONArray items = payload.optJSONArray("items");
            List<RemoteExtension> result = new ArrayList<>();
            if (items == null) {
                return result;
            }

            for (int i = 0; i < items.length(); i++) {
                JSONObject item = items.optJSONObject(i);
                if (item == null || item.optInt("enabled", 1) == 0) {
                    continue;
                }

                String extensionId = item.optString("extension_id");
                if (extensionId.startsWith("yanzi-")) {
                    continue;
                }

                try {
                    JSONObject detail = getJson(baseUrl, "/v1/extensions/" + encodePath(extensionId), token, "读取扩展详情");
                    JSONObject manifest = detail.optJSONObject("manifest");
                    String name = firstNonEmpty(detail.optString("display_name"), manifest == null ? "" : manifest.optString("name"), extensionId);
                    String description = firstNonEmpty(detail.optString("description"), manifest == null ? "" : manifest.optString("description"));
                    String icon = firstNonEmpty(detail.optString("icon"), manifest == null ? "" : manifest.optString("icon"));
                    result.add(new RemoteExtension(extensionId, name, description, icon));
                } catch (Exception ignored) {
                    result.add(new RemoteExtension(extensionId, extensionId, "扩展详情暂不可用，仍可尝试远程执行。", ""));
                }
            }
            return result;
        }

        static JSONObject fetchYanmState(String baseUrl, String token) throws Exception {
            JSONObject payload = getJson(baseUrl, "/v1/me/yanm-state", token, "读取燕幕");
            JSONObject yanm = payload.optJSONObject("yanm");
            if (yanm == null) {
                throw new IllegalStateException("账号云端没有燕幕数据。");
            }
            return yanm;
        }

        static JSONObject putYanmState(String baseUrl, String token, JSONObject yanm) throws Exception {
            JSONObject payload = new JSONObject()
                .put("updatedAtUtc", new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.ROOT).format(new Date()))
                .put("yanm", yanm);
            return putJson(baseUrl, "/v1/me/yanm-state", payload, token, "同步燕幕");
        }

        private static JSONObject putJson(String baseUrl, String path, JSONObject payload, String token, String action) throws Exception {
            HttpURLConnection connection = (HttpURLConnection) new URL(baseUrl + path).openConnection();
            connection.setRequestMethod("PUT");
            connection.setConnectTimeout(15000);
            connection.setReadTimeout(15000);
            connection.setDoOutput(true);
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            connection.setRequestProperty("Accept", "application/json");
            if (token != null && !token.trim().isEmpty()) {
                connection.setRequestProperty("Authorization", "Bearer " + token);
            }

            try (OutputStreamWriter writer = new OutputStreamWriter(connection.getOutputStream(), StandardCharsets.UTF_8)) {
                writer.write(payload.toString());
            }

            String body = readBody(connection);
            if (connection.getResponseCode() < 200 || connection.getResponseCode() >= 300) {
                String message = body;
                try {
                    message = new JSONObject(body).optString("message", body);
                } catch (Exception ignored) {
                }
                throw new IllegalStateException(formatError(action, path, connection.getResponseCode(), message));
            }

            return body.trim().isEmpty() ? new JSONObject() : new JSONObject(body);
        }

        private static JSONObject postJson(String baseUrl, String path, JSONObject payload, String token, String action) throws Exception {
            HttpURLConnection connection = (HttpURLConnection) new URL(baseUrl + path).openConnection();
            connection.setRequestMethod("POST");
            connection.setConnectTimeout(15000);
            connection.setReadTimeout(15000);
            connection.setDoOutput(true);
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            connection.setRequestProperty("Accept", "application/json");
            if (token != null && !token.trim().isEmpty()) {
                connection.setRequestProperty("Authorization", "Bearer " + token);
            }

            try (OutputStreamWriter writer = new OutputStreamWriter(connection.getOutputStream(), StandardCharsets.UTF_8)) {
                writer.write(payload.toString());
            }

            String body = readBody(connection);
            if (connection.getResponseCode() < 200 || connection.getResponseCode() >= 300) {
                String message = body;
                try {
                    message = new JSONObject(body).optString("message", body);
                } catch (Exception ignored) {
                }
                throw new IllegalStateException(formatError(action, path, connection.getResponseCode(), message));
            }

            return new JSONObject(body);
        }

        private static JSONObject getJson(String baseUrl, String path, String token, String action) throws Exception {
            HttpURLConnection connection = (HttpURLConnection) new URL(baseUrl + path).openConnection();
            connection.setRequestMethod("GET");
            connection.setConnectTimeout(15000);
            connection.setReadTimeout(15000);
            connection.setRequestProperty("Accept", "application/json");
            if (token != null && !token.trim().isEmpty()) {
                connection.setRequestProperty("Authorization", "Bearer " + token);
            }

            String body = readBody(connection);
            if (connection.getResponseCode() < 200 || connection.getResponseCode() >= 300) {
                String message = body;
                try {
                    message = new JSONObject(body).optString("message", body);
                } catch (Exception ignored) {
                }
                throw new IllegalStateException(formatError(action, path, connection.getResponseCode(), message));
            }

            return new JSONObject(body);
        }

        private static String encodePath(String value) {
            return value.replace(" ", "%20").replace("/", "%2F");
        }

        private static String formatError(String action, String path, int statusCode, String message) {
            String trimmed = message == null ? "" : message.trim();
            if (statusCode == 404 && trimmed.toLowerCase().contains("route not found")) {
                return action + "接口不存在，请确认云端地址是 " + DEFAULT_BASE_URL + "，并确认 Worker 已发布移动端接口：" + path;
            }
            if (trimmed.isEmpty()) {
                return action + "失败，HTTP " + statusCode;
            }
            return trimmed;
        }

        private static String readBody(HttpURLConnection connection) throws Exception {
            InputStream stream = connection.getResponseCode() >= 200 && connection.getResponseCode() < 300
                ? connection.getInputStream()
                : connection.getErrorStream();
            StringBuilder builder = new StringBuilder();
            try (BufferedReader reader = new BufferedReader(new InputStreamReader(stream, StandardCharsets.UTF_8))) {
                String line;
                while ((line = reader.readLine()) != null) {
                    builder.append(line);
                }
            }
            return builder.toString();
        }
    }
}
