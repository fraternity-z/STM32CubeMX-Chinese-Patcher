package com.codex.cubemx.zh;

import java.awt.Button;
import java.awt.Checkbox;
import java.awt.Component;
import java.awt.Container;
import java.awt.Dialog;
import java.awt.Frame;
import java.awt.Label;
import java.awt.Window;
import java.util.Collections;
import java.util.IdentityHashMap;
import java.util.Objects;
import java.util.Set;
import java.util.function.Consumer;
import javax.swing.AbstractButton;
import javax.swing.ButtonModel;
import javax.swing.JComponent;
import javax.swing.JLabel;
import javax.swing.JMenu;
import javax.swing.JProgressBar;
import javax.swing.JTabbedPane;
import javax.swing.JTable;
import javax.swing.JToolTip;
import javax.swing.SwingUtilities;
import javax.swing.border.Border;
import javax.swing.border.CompoundBorder;
import javax.swing.border.TitledBorder;
import javax.swing.event.ChangeListener;
import javax.swing.table.TableColumnModel;

public final class SwingTranslator {
    private static final String BUTTON_TRANSLATION_PROPERTY = "com.codex.cubemx.zh.buttonTranslation";
    private static final String BREADCRUMB_BUTTON_CLASS = "com.st.microxplorer.stobjects.STBreadCrumbButton";
    private final TranslationDictionary dictionary;

    public SwingTranslator(TranslationDictionary dictionary) {
        this.dictionary = Objects.requireNonNull(dictionary, "dictionary");
    }

    public int translateAllWindows() {
        requireEventDispatchThread();
        var translated = 0;
        for (var window : Window.getWindows()) {
            translated += translateTree(window);
        }
        return translated;
    }

    public int translateTree(Component root) {
        requireEventDispatchThread();
        Objects.requireNonNull(root, "root");
        Set<Component> visited = Collections.newSetFromMap(new IdentityHashMap<>());
        return translateComponent(root, visited);
    }

    private int translateComponent(Component component, Set<Component> visited) {
        if (!visited.add(component)) {
            return 0;
        }

        var translated = 0;
        if (component instanceof Frame frame) {
            translated += replace(frame.getTitle(), frame::setTitle);
        } else if (component instanceof Dialog dialog) {
            translated += replace(dialog.getTitle(), dialog::setTitle);
        }

        if (component instanceof AbstractButton button) {
            translated += translateButton(button);
        } else if (component instanceof JLabel label) {
            translated += replace(label.getText(), label::setText);
        } else if (component instanceof Label label) {
            translated += replace(label.getText(), label::setText);
        } else if (component instanceof Button button) {
            translated += replace(button.getLabel(), button::setLabel);
        } else if (component instanceof Checkbox checkbox) {
            translated += replace(checkbox.getLabel(), checkbox::setLabel);
        } else if (component instanceof JToolTip toolTip) {
            translated += replace(toolTip.getTipText(), toolTip::setTipText);
        }

        if (component instanceof JComponent swingComponent) {
            translated += replace(swingComponent.getToolTipText(), swingComponent::setToolTipText);
            translated += translateBorder(swingComponent.getBorder());
        }
        if (component instanceof JTabbedPane tabs) {
            translated += translateTabs(tabs);
        }
        if (component instanceof JTable table) {
            translated += translateColumns(table.getColumnModel());
        }
        if (component instanceof JProgressBar progress && progress.isStringPainted()) {
            translated += replace(progress.getString(), progress::setString);
        }
        if (component instanceof JMenu menu) {
            for (var child : menu.getMenuComponents()) {
                translated += translateComponent(child, visited);
            }
        }
        if (component instanceof Container container) {
            for (var child : container.getComponents()) {
                translated += translateComponent(child, visited);
            }
        }
        return translated;
    }

    private int translateTabs(JTabbedPane tabs) {
        var translated = 0;
        for (var index = 0; index < tabs.getTabCount(); index++) {
            var header = tabs.getTabComponentAt(index);
            if (header != null && !(header instanceof TranslatedTabLabel)) {
                continue;
            }

            var source = tabs.getTitleAt(index);
            var target = dictionary.translate(source);
            if (Objects.equals(source, target)) {
                if (header instanceof TranslatedTabLabel) {
                    tabs.setTabComponentAt(index, null);
                    translated++;
                }
                continue;
            }

            var label = header instanceof TranslatedTabLabel existing
                ? existing
                : new TranslatedTabLabel();
            translated += label.update(
                tabs, index, target, dictionary.translate(tabs.getToolTipTextAt(index)));
            if (header == null) {
                tabs.setTabComponentAt(index, label);
            }
        }
        return translated;
    }

    private int translateColumns(TableColumnModel columns) {
        var translated = 0;
        for (var index = 0; index < columns.getColumnCount(); index++) {
            var column = columns.getColumn(index);
            if (column.getHeaderValue() instanceof String text) {
                translated += replace(text, column::setHeaderValue);
            }
        }
        return translated;
    }

    private int translateButton(AbstractButton button) {
        var property = button.getClientProperty(BUTTON_TRANSLATION_PROPERTY);
        var current = property instanceof ButtonTranslation translation ? translation : null;
        var text = button.getText();
        if (current != null && current.matches(text)) {
            current.ensureCurrentModel();
            current.syncToModelState();
            return 0;
        }
        if (current != null) {
            current.detach();
            button.putClientProperty(BUTTON_TRANSLATION_PROPERTY, null);
        }

        var source = sourceTextForSetter(button, text);
        var target = dictionary.translate(source);
        if (Objects.equals(source, target)) {
            return 0;
        }

        var translation = new ButtonTranslation(button, text, source, target);
        button.putClientProperty(BUTTON_TRANSLATION_PROPERTY, translation);
        translation.attach();
        translation.showTarget();
        return 1;
    }

    private static String sourceTextForSetter(AbstractButton button, String text) {
        return text != null && BREADCRUMB_BUTTON_CLASS.equals(button.getClass().getName())
            ? text.stripTrailing()
            : text;
    }

    private int translateBorder(Border border) {
        if (border instanceof TitledBorder titledBorder) {
            return replace(titledBorder.getTitle(), titledBorder::setTitle)
                + translateBorder(titledBorder.getBorder());
        }
        if (border instanceof CompoundBorder compoundBorder) {
            return translateBorder(compoundBorder.getOutsideBorder())
                + translateBorder(compoundBorder.getInsideBorder());
        }
        return 0;
    }

    private int replace(String source, Consumer<String> setter) {
        var target = dictionary.translate(source);
        if (Objects.equals(source, target)) {
            return 0;
        }
        setter.accept(target);
        return 1;
    }

    private static void requireEventDispatchThread() {
        if (!SwingUtilities.isEventDispatchThread()) {
            throw new IllegalStateException("Swing translation must run on the AWT event dispatch thread");
        }
    }

    private static final class TranslatedTabLabel extends JLabel {
        TranslatedTabLabel() {
            super("", JLabel.CENTER);
            setOpaque(false);
        }

        int update(JTabbedPane tabs, int index, String text, String toolTip) {
            var textChanged = !Objects.equals(getText(), text);
            setText(text);
            setIcon(tabs.getIconAt(index));
            setDisabledIcon(tabs.getDisabledIconAt(index));
            setEnabled(tabs.isEnabled() && tabs.isEnabledAt(index));
            setFont(tabs.getFont());
            setForeground(tabs.getForegroundAt(index));
            setBackground(tabs.getBackgroundAt(index));
            setComponentOrientation(tabs.getComponentOrientation());
            setToolTipText(toolTip);
            setDisplayedMnemonic(tabs.getMnemonicAt(index));
            return textChanged ? 1 : 0;
        }
    }

    private static final class ButtonTranslation {
        private final AbstractButton button;
        private final String sourceRequest;
        private final String targetRequest;
        private final ChangeListener listener = event -> syncToModelState();
        private String sourceActual;
        private String targetActual;
        private ButtonModel model;

        ButtonTranslation(AbstractButton button, String sourceActual, String sourceRequest, String targetRequest) {
            this.button = button;
            this.sourceActual = sourceActual;
            this.sourceRequest = sourceRequest;
            this.targetRequest = targetRequest;
        }

        boolean matches(String text) {
            return Objects.equals(text, sourceActual) || Objects.equals(text, targetActual);
        }

        void attach() {
            model = button.getModel();
            model.addChangeListener(listener);
        }

        void detach() {
            if (model != null) {
                model.removeChangeListener(listener);
            }
        }

        void ensureCurrentModel() {
            if (model != button.getModel()) {
                detach();
                attach();
            }
        }

        void syncToModelState() {
            if (model.isArmed() && model.isPressed()) {
                showSource();
            } else {
                showTarget();
            }
        }

        void showSource() {
            if (!Objects.equals(button.getText(), sourceActual)) {
                button.setText(sourceRequest);
                sourceActual = button.getText();
            }
        }

        void showTarget() {
            if (!Objects.equals(button.getText(), targetActual)) {
                button.setText(targetRequest);
                targetActual = button.getText();
            }
        }
    }
}
