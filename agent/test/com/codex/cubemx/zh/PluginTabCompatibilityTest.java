package com.codex.cubemx.zh;

import com.st.microxplorer.maingui.MainPanel;
import java.nio.file.Path;
import java.util.concurrent.atomic.AtomicReference;
import javax.swing.JFrame;
import javax.swing.JLabel;
import javax.swing.SwingUtilities;

public final class PluginTabCompatibilityTest {
    private PluginTabCompatibilityTest() {
    }

    public static void main(String[] args) throws Exception {
        var dictionary = TranslationDictionary.load(Path.of(args[0]));
        var failure = new AtomicReference<Throwable>();

        SwingUtilities.invokeAndWait(() -> {
            var frame = new JFrame();
            try {
                var mainPanel = new MainPanel();
                frame.add(mainPanel);

                PluginTabCompatibility.install(dictionary);
                var tab = mainPanel.getTabComponent();
                require(tab.getComponentCount() == 2, "未插入内部英文标签");
                require("Project Manager".equals(mainPanel.getSelectedPluginView()), "CubeMX 未读取到英文插件名");

                var internalLabel = (JLabel) tab.getComponent(0);
                var visibleLabel = (JLabel) tab.getComponent(1);
                require(!internalLabel.isVisible(), "内部英文标签不应可见");
                require("工程管理器".equals(internalLabel.getText()), "普通读取应保持中文");
                require("工程管理器".equals(visibleLabel.getText()), "可见标签的汉化被改变");

                internalLabel.setText("被重复扫描修改");
                require("工程管理器".equals(internalLabel.getText()), "内部标签未抵抗重复翻译");

                PluginTabCompatibility.install(dictionary);
                require(tab.getComponentCount() == 2, "重复扫描插入了多余标签");
                System.out.println("PLUGIN_TAB_COMPATIBILITY_PASS");
            } catch (Throwable exception) {
                failure.set(exception);
            } finally {
                frame.dispose();
            }
        });

        if (failure.get() != null) {
            throw new AssertionError(failure.get());
        }
        System.exit(0);
    }

    private static void require(boolean condition, String message) {
        if (!condition) {
            throw new AssertionError(message);
        }
    }
}
