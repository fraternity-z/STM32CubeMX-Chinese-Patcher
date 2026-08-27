package com.codex.cubemx.zh;

import com.st.microxplorer.maingui.MainPanel;
import java.nio.file.Path;
import java.util.concurrent.atomic.AtomicReference;
import javax.swing.BorderFactory;
import javax.swing.JComboBox;
import javax.swing.JFrame;
import javax.swing.JLabel;
import javax.swing.JList;
import javax.swing.JPanel;
import javax.swing.ListCellRenderer;
import javax.swing.SwingUtilities;
import javax.swing.border.TitledBorder;

public final class PluginTabCompatibilityTest {
    private static final String CROSS_SELECTOR_DISCLAIMER =
        "The information presented in this cross-reference tool is intended to help you narrow your search of ST products based on similarity to other available products. The information is based on data published by other semiconductor manufacturers and might contain errors. ST provides the information “as is” and does not make any representations or warranties as to its accuracy or suitability for any particular purpose. ST recommends that you make your purchase decision based on your review of ST’s datasheets and other product documentation. Any pricing information is an estimate for budgetary purposes only.";

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
                verifySelectorTextCompatibility(dictionary);
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

    private static void verifySelectorTextCompatibility(TranslationDictionary dictionary) {
        require(
            "示例列表：0 项（无匹配）".equals(SelectorTextCompatibility.translateText(
                "Examples List: 0 item (No Match)",
                dictionary)),
            "示例列表空结果未汉化");
        require(
            "示例列表：1 项".equals(SelectorTextCompatibility.translateText(
                "Examples List: 1 item",
                dictionary)),
            "示例列表单数未汉化");
        require(
            " \t示例列表：42 项\u00a0".equals(SelectorTextCompatibility.translateText(
                " \tExamples List: 42 items\u00a0",
                dictionary)),
            "示例列表复数或外围空白未正确汉化");
        require(
            "Unrelated 42 items".equals(SelectorTextCompatibility.translateText(
                "Unrelated 42 items",
                dictionary)),
            "无关动态文本不应改变");

        var root = new JPanel();
        var examplesList = new JLabel("Examples List: 11400 items");
        root.add(examplesList);

        var candidatesPanel = new JPanel();
        candidatesPanel.setBorder(BorderFactory.createTitledBorder(
            "Matching ST candidates (12)"));
        root.add(candidatesPanel);

        var crossSelectorHtml = new JLabel(
            "<html><p style=\"color: #123456;\">"
                + "Select your MCU using the pick-lists on the left (you can filter by vendor)"
                + "</p><p><strong>Note:</strong><br/>"
                + CROSS_SELECTOR_DISCLAIMER
                + "</p></html>");
        root.add(crossSelectorHtml);

        var vendorComboBox = new JComboBox<>(new String[] {
            "-- All vendors --",
            "Microchip",
        });
        root.add(vendorComboBox);
        var unrelatedComboBox = new JComboBox<>(new String[] { "Yes", "No" });
        var unrelatedRenderer = unrelatedComboBox.getRenderer();
        root.add(unrelatedComboBox);

        var translated = SelectorTextCompatibility.translateTree(root, dictionary);
        require(translated >= 4, "选择器兼容扫描未覆盖全部目标组件");
        require("示例列表：11400 项".equals(examplesList.getText()), "示例数量标题未汉化");
        require(
            "匹配的 ST 候选型号（12）".equals(
                ((TitledBorder) candidatesPanel.getBorder()).getTitle()),
            "候选型号动态标题未汉化");
        require(crossSelectorHtml.getText().contains("请使用左侧选择列表选择 MCU（可按厂商筛选）"),
            "交叉选择器操作引导未汉化");
        require(crossSelectorHtml.getText().contains("<strong>注意：</strong>"),
            "交叉选择器说明标题未汉化");
        require(crossSelectorHtml.getText().contains("本交叉参考工具提供的信息"),
            "交叉选择器说明正文未汉化");
        require(crossSelectorHtml.getText().contains("color: #123456"),
            "交叉选择器 HTML 样式被改变");

        require("-- All vendors --".equals(vendorComboBox.getSelectedItem()),
            "厂商下拉框业务值被修改");
        require("-- 所有厂商 --".equals(renderItem(vendorComboBox, 0)),
            "厂商下拉框默认项未汉化");
        require("Microchip".equals(renderItem(vendorComboBox, 1)),
            "厂商产品数据不应汉化");
        require(unrelatedRenderer == unrelatedComboBox.getRenderer(),
            "无关下拉框不应安装兼容渲染器");

        var renderer = vendorComboBox.getRenderer();
        require(SelectorTextCompatibility.translateTree(root, dictionary) == 0,
            "重复扫描不应再次修改组件");
        require(renderer == vendorComboBox.getRenderer(), "重复扫描替换了下拉框渲染器");
    }

    @SuppressWarnings({ "rawtypes", "unchecked" })
    private static String renderItem(JComboBox<?> comboBox, int index) {
        ListCellRenderer renderer = comboBox.getRenderer();
        var value = comboBox.getItemAt(index);
        var component = renderer.getListCellRendererComponent(
            new JList<>(),
            value,
            index,
            false,
            false);
        require(component instanceof JLabel, "厂商下拉框渲染器未返回标签");
        return ((JLabel) component).getText();
    }

    private static void require(boolean condition, String message) {
        if (!condition) {
            throw new AssertionError(message);
        }
    }
}
