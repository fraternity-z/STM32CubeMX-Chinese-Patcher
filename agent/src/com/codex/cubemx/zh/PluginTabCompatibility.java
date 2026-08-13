package com.codex.cubemx.zh;

import java.awt.Component;
import java.awt.Container;
import java.awt.Dimension;
import java.awt.Window;
import java.lang.reflect.Method;
import java.util.ArrayList;
import java.util.Collections;
import java.util.IdentityHashMap;
import java.util.List;
import java.util.Objects;
import java.util.Set;
import javax.swing.JLabel;
import javax.swing.JPanel;
import javax.swing.JTabbedPane;
import javax.swing.SwingUtilities;
import javax.swing.Timer;

final class PluginTabCompatibility {
    private static final int PERIODIC_SCAN_MILLIS = 1200;
    private static final String MAIN_PANEL_CLASS = "com.st.microxplorer.maingui.MainPanel";
    private static final String PLUGIN_MANAGE_CLASS = "com.st.microxplorer.plugin.PluginManage";
    private static final StackWalker STACK_WALKER =
        StackWalker.getInstance(StackWalker.Option.RETAIN_CLASS_REFERENCE);
    private static Timer periodicTimer;

    private PluginTabCompatibility() {
    }

    static void install(TranslationDictionary dictionary) {
        Objects.requireNonNull(dictionary, "dictionary");
        if (!SwingUtilities.isEventDispatchThread()) {
            SwingUtilities.invokeLater(() -> install(dictionary));
            return;
        }

        scan(dictionary);
        if (periodicTimer == null) {
            periodicTimer = new Timer(PERIODIC_SCAN_MILLIS, event -> scan(dictionary));
            periodicTimer.setRepeats(true);
            periodicTimer.start();
        }
    }

    private static void scan(TranslationDictionary dictionary) {
        try {
            var pluginNames = loadPluginNames();
            if (pluginNames.isEmpty()) {
                return;
            }

            Set<Component> visited = Collections.newSetFromMap(new IdentityHashMap<>());
            for (var window : Window.getWindows()) {
                protectTree(window, dictionary, pluginNames, visited);
            }
        } catch (RuntimeException exception) {
            System.err.println(
                "[STM32CubeMX zh-agent] Plugin tab compatibility scan failed: "
                    + exception.getClass().getSimpleName());
        }
    }

    private static int protectTree(
        Component component,
        TranslationDictionary dictionary,
        List<String> pluginNames,
        Set<Component> visited) {
        if (!visited.add(component)) {
            return 0;
        }

        var protectedTabs = 0;
        if (component instanceof JTabbedPane tabbedPane && isInsideMainPanel(tabbedPane)) {
            protectedTabs += protectPluginTabs(tabbedPane, dictionary, pluginNames);
        }

        if (component instanceof Container container) {
            for (var child : container.getComponents()) {
                protectedTabs += protectTree(child, dictionary, pluginNames, visited);
            }
        }

        return protectedTabs;
    }

    private static boolean isInsideMainPanel(Component component) {
        for (var current = component.getParent(); current != null; current = current.getParent()) {
            if (MAIN_PANEL_CLASS.equals(current.getClass().getName())) {
                return true;
            }
        }
        return false;
    }

    private static int protectPluginTabs(
        JTabbedPane tabbedPane,
        TranslationDictionary dictionary,
        List<String> pluginNames) {
        var protectedTabs = 0;
        for (var index = 0; index < tabbedPane.getTabCount(); index++) {
            var tabComponent = tabbedPane.getTabComponentAt(index);
            if (!(tabComponent instanceof JPanel panel) || hasOriginalNameLabel(panel)) {
                continue;
            }

            var originalName = findOriginalPluginName(panel, dictionary, pluginNames);
            if (originalName == null) {
                continue;
            }

            panel.add(
                new OriginalPluginNameLabel(originalName, dictionary.translate(originalName)),
                0);
            panel.revalidate();
            panel.repaint();
            protectedTabs++;
        }
        return protectedTabs;
    }

    private static boolean hasOriginalNameLabel(JPanel panel) {
        for (var component : panel.getComponents()) {
            if (component instanceof OriginalPluginNameLabel) {
                return true;
            }
        }
        return false;
    }

    private static String findOriginalPluginName(
        JPanel panel,
        TranslationDictionary dictionary,
        List<String> pluginNames) {
        for (var component : panel.getComponents()) {
            if (!(component instanceof JLabel label) || label.getIcon() != null) {
                continue;
            }

            var labelText = label.getText();
            for (var pluginName : pluginNames) {
                if (pluginName.equals(labelText)
                    || Objects.equals(dictionary.translate(pluginName), labelText)) {
                    return pluginName;
                }
            }
        }
        return null;
    }

    private static List<String> loadPluginNames() {
        try {
            var pluginManage = Class.forName(PLUGIN_MANAGE_CLASS);
            Method getPlugins = pluginManage.getMethod("getPlugins");
            var plugins = getPlugins.invoke(null);
            if (!(plugins instanceof Iterable<?> iterable)) {
                return List.of();
            }

            var names = new ArrayList<String>();
            for (var plugin : iterable) {
                if (plugin == null) {
                    continue;
                }
                Method getName = plugin.getClass().getMethod("getName");
                var name = getName.invoke(plugin);
                if (name instanceof String text && !text.isBlank()) {
                    names.add(text);
                }
            }
            return names;
        } catch (ReflectiveOperationException | LinkageError exception) {
            return List.of();
        }
    }

    private static final class OriginalPluginNameLabel extends JLabel {
        private final String originalText;
        private final String translatedText;

        OriginalPluginNameLabel(String originalText, String translatedText) {
            this.originalText = Objects.requireNonNull(originalText, "originalText");
            this.translatedText = Objects.requireNonNull(translatedText, "translatedText");
            super.setText(translatedText);
            setVisible(false);
            setMinimumSize(new Dimension(0, 0));
            setPreferredSize(new Dimension(0, 0));
            setMaximumSize(new Dimension(0, 0));
        }

        @Override
        public String getText() {
            if (originalText == null || translatedText == null) {
                return super.getText();
            }

            var calledByPluginLookup = STACK_WALKER.walk(frames -> frames.anyMatch(frame ->
                MAIN_PANEL_CLASS.equals(frame.getClassName())
                    && "getSelectedPluginView".equals(frame.getMethodName())));
            return calledByPluginLookup ? originalText : translatedText;
        }

        @Override
        public void setText(String text) {
            if (originalText == null) {
                super.setText(text);
            }
        }
    }
}
