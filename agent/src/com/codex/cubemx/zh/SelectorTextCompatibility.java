package com.codex.cubemx.zh;

import java.awt.Component;
import java.awt.Container;
import java.awt.Window;
import java.util.Collections;
import java.util.IdentityHashMap;
import java.util.Objects;
import java.util.Set;
import java.util.regex.Pattern;
import javax.swing.DefaultListCellRenderer;
import javax.swing.JComboBox;
import javax.swing.JComponent;
import javax.swing.JLabel;
import javax.swing.JList;
import javax.swing.ListCellRenderer;
import javax.swing.SwingUtilities;
import javax.swing.border.Border;
import javax.swing.border.CompoundBorder;
import javax.swing.border.TitledBorder;

final class SelectorTextCompatibility {
    private static final String COUNT_TOKEN = "{count}";
    private static final String ALL_VENDORS_ITEM = "-- All vendors --";
    private static final String EXAMPLE_COUNT_ONE_TEMPLATE =
        "Examples List: {count} item";
    private static final String EXAMPLE_COUNT_MANY_TEMPLATE =
        "Examples List: {count} items";
    private static final String EXAMPLE_COUNT_NONE_TEMPLATE =
        "Examples List: {count} item (No Match)";
    private static final String MATCHING_CANDIDATES_TEMPLATE =
        "Matching ST candidates ({count})";
    private static final Pattern EXAMPLE_COUNT_PATTERN = Pattern.compile(
        "Examples List: ([0-9]+) (item|items)( \\(No Match\\))?");
    private static final Pattern MATCHING_CANDIDATES_PATTERN = Pattern.compile(
        "Matching ST candidates \\(([0-9]+)\\)");
    private static final String[] CROSS_SELECTOR_HTML_TEXTS = {
        "Select your MCU using the pick-lists on the left (you can filter by vendor)",
        "Note:",
        "The information presented in this cross-reference tool is intended to help you narrow your search of ST products based on similarity to other available products. The information is based on data published by other semiconductor manufacturers and might contain errors. ST provides the information “as is” and does not make any representations or warranties as to its accuracy or suitability for any particular purpose. ST recommends that you make your purchase decision based on your review of ST’s datasheets and other product documentation. Any pricing information is an estimate for budgetary purposes only.",
    };

    private SelectorTextCompatibility() {
    }

    static int translateAllWindows(TranslationDictionary dictionary) {
        Objects.requireNonNull(dictionary, "dictionary");
        requireEventDispatchThread();

        Set<Component> visited = Collections.newSetFromMap(new IdentityHashMap<>());
        var translated = 0;
        for (var window : Window.getWindows()) {
            translated += translateComponent(window, dictionary, visited);
        }
        return translated;
    }

    static int translateTree(Component root, TranslationDictionary dictionary) {
        Objects.requireNonNull(root, "root");
        Objects.requireNonNull(dictionary, "dictionary");
        requireEventDispatchThread();

        Set<Component> visited = Collections.newSetFromMap(new IdentityHashMap<>());
        return translateComponent(root, dictionary, visited);
    }

    static String translateText(String text, TranslationDictionary dictionary) {
        Objects.requireNonNull(dictionary, "dictionary");
        if (text == null) {
            return null;
        }

        var directTranslation = dictionary.translate(text);
        if (!Objects.equals(text, directTranslation)) {
            return directTranslation;
        }

        var start = 0;
        var end = text.length();
        while (start < end && isLayoutWhitespace(text.charAt(start))) {
            start++;
        }
        while (end > start && isLayoutWhitespace(text.charAt(end - 1))) {
            end--;
        }

        var source = text.substring(start, end);
        var translation = translateDynamicText(source, dictionary);
        if (Objects.equals(source, translation)) {
            return text;
        }
        return text.substring(0, start) + translation + text.substring(end);
    }

    private static int translateComponent(
        Component component,
        TranslationDictionary dictionary,
        Set<Component> visited) {
        if (!visited.add(component)) {
            return 0;
        }

        var translated = 0;
        if (component instanceof JLabel label) {
            var source = label.getText();
            var translation = translateText(source, dictionary);
            if (!Objects.equals(source, translation)) {
                label.setText(translation);
                translated++;
            }
        }

        if (component instanceof JComboBox<?> comboBox) {
            translated += installComboBoxRenderer(comboBox, dictionary);
        }

        if (component instanceof JComponent swingComponent) {
            translated += translateBorder(swingComponent.getBorder(), dictionary);
        }

        if (component instanceof Container container) {
            for (var child : container.getComponents()) {
                translated += translateComponent(child, dictionary, visited);
            }
        }
        return translated;
    }

    private static String translateDynamicText(String text, TranslationDictionary dictionary) {
        var exampleCount = EXAMPLE_COUNT_PATTERN.matcher(text);
        if (exampleCount.matches()) {
            String template;
            if (exampleCount.group(3) != null) {
                template = EXAMPLE_COUNT_NONE_TEMPLATE;
            } else if ("items".equals(exampleCount.group(2))) {
                template = EXAMPLE_COUNT_MANY_TEMPLATE;
            } else {
                template = EXAMPLE_COUNT_ONE_TEMPLATE;
            }
            return applyCountTemplate(text, template, exampleCount.group(1), dictionary);
        }

        var matchingCandidates = MATCHING_CANDIDATES_PATTERN.matcher(text);
        if (matchingCandidates.matches()) {
            return applyCountTemplate(
                text,
                MATCHING_CANDIDATES_TEMPLATE,
                matchingCandidates.group(1),
                dictionary);
        }

        if (!text.startsWith("<html>")) {
            return text;
        }

        var translation = text;
        for (var source : CROSS_SELECTOR_HTML_TEXTS) {
            var target = dictionary.translate(source);
            if (!Objects.equals(source, target)) {
                translation = translation.replace(source, target);
            }
        }
        return translation;
    }

    private static String applyCountTemplate(
        String source,
        String template,
        String count,
        TranslationDictionary dictionary) {
        var translatedTemplate = dictionary.translate(template);
        if (Objects.equals(template, translatedTemplate)) {
            return source;
        }
        return translatedTemplate.replace(COUNT_TOKEN, count);
    }

    private static int translateBorder(Border border, TranslationDictionary dictionary) {
        if (border instanceof TitledBorder titledBorder) {
            var source = titledBorder.getTitle();
            var translation = translateText(source, dictionary);
            var translated = 0;
            if (!Objects.equals(source, translation)) {
                titledBorder.setTitle(translation);
                translated++;
            }
            return translated + translateBorder(titledBorder.getBorder(), dictionary);
        }

        if (border instanceof CompoundBorder compoundBorder) {
            return translateBorder(compoundBorder.getOutsideBorder(), dictionary)
                + translateBorder(compoundBorder.getInsideBorder(), dictionary);
        }
        return 0;
    }

    @SuppressWarnings({ "rawtypes", "unchecked" })
    private static int installComboBoxRenderer(
        JComboBox<?> comboBox,
        TranslationDictionary dictionary) {
        var hasAllVendorsItem = false;
        var model = comboBox.getModel();
        for (var index = 0; index < model.getSize(); index++) {
            if (Objects.equals(ALL_VENDORS_ITEM, model.getElementAt(index))) {
                hasAllVendorsItem = true;
                break;
            }
        }
        if (!hasAllVendorsItem) {
            return 0;
        }

        ListCellRenderer renderer = comboBox.getRenderer();
        if (renderer instanceof TranslatingComboBoxRenderer translatingRenderer) {
            if (translatingRenderer.dictionary == dictionary) {
                return 0;
            }
            renderer = translatingRenderer.delegate;
        }
        if (renderer == null) {
            renderer = new DefaultListCellRenderer();
        }

        ((JComboBox) comboBox).setRenderer(
            new TranslatingComboBoxRenderer(renderer, dictionary));
        return 1;
    }

    private static boolean isLayoutWhitespace(char value) {
        return Character.isWhitespace(value) || Character.isSpaceChar(value);
    }

    private static void requireEventDispatchThread() {
        if (!SwingUtilities.isEventDispatchThread()) {
            throw new IllegalStateException(
                "Selector text compatibility must run on the AWT event dispatch thread");
        }
    }

    @SuppressWarnings("rawtypes")
    private static final class TranslatingComboBoxRenderer implements ListCellRenderer<Object> {
        private final ListCellRenderer delegate;
        private final TranslationDictionary dictionary;

        TranslatingComboBoxRenderer(
            ListCellRenderer delegate,
            TranslationDictionary dictionary) {
            this.delegate = Objects.requireNonNull(delegate, "delegate");
            this.dictionary = Objects.requireNonNull(dictionary, "dictionary");
        }

        @Override
        @SuppressWarnings("unchecked")
        public Component getListCellRendererComponent(
            JList<?> list,
            Object value,
            int index,
            boolean isSelected,
            boolean cellHasFocus) {
            var component = delegate.getListCellRendererComponent(
                list,
                value,
                index,
                isSelected,
                cellHasFocus);
            if (component instanceof JLabel label && value instanceof String text) {
                label.setText(dictionary.translate(text));
            }
            return component;
        }
    }
}
