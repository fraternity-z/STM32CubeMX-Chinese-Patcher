package com.codex.cubemx.zh;

import com.st.microxplorer.maingui.MainPanel;
import java.awt.Color;
import java.awt.Component;
import java.awt.event.KeyEvent;
import java.awt.image.BufferedImage;
import java.nio.file.Path;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;
import javax.swing.BorderFactory;
import javax.swing.DefaultButtonModel;
import javax.swing.ImageIcon;
import javax.swing.JButton;
import javax.swing.JComboBox;
import javax.swing.JFrame;
import javax.swing.JLabel;
import javax.swing.JList;
import javax.swing.JMenu;
import javax.swing.JMenuBar;
import javax.swing.JMenuItem;
import javax.swing.JPanel;
import javax.swing.JTabbedPane;
import javax.swing.JTable;
import javax.swing.JTextField;
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
                verifyTabTitleCompatibility(dictionary);
                verifyTabHeaderLifecycle(dictionary);
                verifyExistingSwingBehavior(dictionary);
                System.out.println("PLUGIN_TAB_COMPATIBILITY_PASS");
            } catch (Throwable exception) {
                failure.set(exception);
            } finally {
                frame.dispose();
            }
        });

        if (failure.get() != null) {
            failure.get().printStackTrace();
            System.exit(1);
        }
        System.exit(0);
    }

    private static void verifyTabTitleCompatibility(TranslationDictionary dictionary) {
        var translator = new SwingTranslator(dictionary);
        var tabs = new JTabbedPane();
        var titles = new String[] {
            "Parameter Settings", "GPIO Settings", "NVIC Settings", "DMA Settings",
        };
        var contents = new JPanel[titles.length];
        var headers = new Component[titles.length];
        for (var index = 0; index < titles.length; index++) {
            contents[index] = new JPanel();
            tabs.addTab(titles[index], contents[index]);
        }
        var icon = new ImageIcon(new BufferedImage(2, 2, BufferedImage.TYPE_INT_ARGB));
        var disabledIcon = new ImageIcon(new BufferedImage(2, 2, BufferedImage.TYPE_INT_ARGB));
        tabs.setIconAt(0, icon);
        tabs.setDisabledIconAt(0, disabledIcon);
        tabs.setEnabledAt(0, false);
        tabs.setForegroundAt(0, Color.BLUE);
        tabs.setToolTipTextAt(0, "Reset");
        tabs.setMnemonicAt(0, KeyEvent.VK_P);
        tabs.setSelectedIndex(2);
        var selectionChanges = new AtomicInteger();
        tabs.addChangeListener(event -> selectionChanges.incrementAndGet());

        for (var scan = 0; scan < 5; scan++) {
            translator.translateTree(tabs);
            for (var title : titles) {
                if (tabs.indexOfTab(title) < 0) {
                    tabs.addTab(title, new JPanel());
                }
            }
            require(tabs.getTabCount() == titles.length, "按英文名称刷新后出现了重复标签页");
            for (var index = 0; index < titles.length; index++) {
                require(titles[index].equals(tabs.getTitleAt(index)), "标签页内部标题不应翻译");
                require(tabs.indexOfTab(titles[index]) == index, "英文标签页查找失效");
                require(tabs.getComponentAt(index) == contents[index], "标签页内容被替换");
                var header = tabLabel(tabs, index);
                require(dictionary.translate(titles[index]).equals(header.getText()), "标签页标题未显示中文");
                if (scan == 0) {
                    headers[index] = header;
                } else {
                    require(headers[index] == header, "重复扫描不应重建标题组件");
                }
            }
            require(translator.translateTree(tabs) == 0, "重复扫描不应再次修改标签页文本");
        }

        var header = tabLabel(tabs, 0);
        require(header.getIcon() == icon, "标签页图标丢失");
        require(header.getDisabledIcon() == disabledIcon, "标签页禁用图标丢失");
        require(!header.isEnabled(), "禁用标签页的标题状态错误");
        require(Color.BLUE.equals(header.getForeground()), "标签页前景色丢失");
        require(tabs.getFont().equals(header.getFont()), "标签页字体丢失");
        require("重置".equals(header.getToolTipText()), "标签页标题提示未翻译");
        require("Reset".equals(tabs.getToolTipTextAt(0)), "标签页内部提示不应改变");
        require(tabs.getMnemonicAt(0) == KeyEvent.VK_P, "标签页快捷键被改变");
        require(tabs.getSelectedIndex() == 2 && selectionChanges.get() == 0, "汉化触发了标签页切换");

        tabs.setEnabledAt(0, true);
        tabs.setIconAt(0, disabledIcon);
        translator.translateTree(tabs);
        require(header.isEnabled() && header.getIcon() == disabledIcon, "标题未同步新的启用状态和图标");
        tabs.setEnabled(false);
        translator.translateTree(tabs);
        require(!tabLabel(tabs, 2).isEnabled(), "整个标签栏禁用时标题仍处于启用状态");
    }

    private static void verifyTabHeaderLifecycle(TranslationDictionary dictionary) {
        var translator = new SwingTranslator(dictionary);
        var tabs = new JTabbedPane();
        tabs.addTab("Parameter Settings", new JPanel());
        tabs.addTab("GPIO Settings", new JPanel());
        translator.translateTree(tabs);
        var originalHeader = tabLabel(tabs, 0);

        tabs.setTitleAt(0, "Configuration");
        translator.translateTree(tabs);
        require(originalHeader == tabs.getTabComponentAt(0), "标题变化不应重建标题组件");
        require("配置".equals(originalHeader.getText()), "标题组件未同步新的标题");
        require("Configuration".equals(tabs.getTitleAt(0)), "新的内部标题被翻译");

        tabs.setTitleAt(0, "Custom Peripheral");
        translator.translateTree(tabs);
        require(tabs.getTabComponentAt(0) == null, "无译文时应恢复默认标题渲染");
        require("Custom Peripheral".equals(tabs.getTitleAt(0)), "无译文的标题被修改");
        tabs.setTitleAt(0, "Parameter Settings");
        translator.translateTree(tabs);
        require("参数设置".equals(tabLabel(tabs, 0).getText()), "恢复可翻译标题后未汉化");

        tabs.removeTabAt(0);
        translator.translateTree(tabs);
        require("GPIO 设置".equals(tabLabel(tabs, 0).getText()), "移除标签页后标题绑定错位");
        tabs.setTabComponentAt(0, null);
        translator.translateTree(tabs);
        require("GPIO Settings".equals(tabs.getTitleAt(0)), "重建标题组件修改了内部标题");
        require("GPIO 设置".equals(tabLabel(tabs, 0).getText()), "未处理宿主重建标题组件");
        tabs.removeAll();
        require(translator.translateTree(tabs) == 0, "移除所有标签页后仍修改旧标题");

        var customHeader = new JPanel();
        var label = new JLabel("DMA Settings");
        var closeButton = new JButton("x");
        customHeader.add(label);
        customHeader.add(closeButton);
        tabs.addTab("DMA Settings", new JPanel());
        tabs.setTabComponentAt(0, customHeader);
        tabs.addTab("STM32F407VG", new JPanel());
        translator.translateTree(tabs);
        require(tabs.getTabComponentAt(0) == customHeader, "宿主自定义标题组件被覆盖");
        require(customHeader.getComponentCount() == 2 && customHeader.getComponent(1) == closeButton,
            "宿主自定义标题组件结构被改变");
        require("DMA 设置".equals(label.getText()), "宿主自定义标题未汉化");
        require(tabs.indexOfTab("DMA Settings") == 0, "自定义标题破坏了内部英文查找");
        require(tabs.getTabComponentAt(1) == null, "未翻译的型号不应安装标题组件");
    }

    private static void verifyExistingSwingBehavior(TranslationDictionary dictionary) {
        var translator = new SwingTranslator(dictionary);
        var root = new JPanel();
        var label = new JLabel("Configuration");
        var button = new JButton("Apply");
        var input = new JTextField("Reset");
        var table = new JTable(new Object[][] { { "Reset" } }, new Object[] { "Configuration" });
        var menuBar = new JMenuBar();
        var menu = new JMenu("Configuration");
        var menuItem = new JMenuItem("Reset");
        menu.add(menuItem);
        menuBar.add(menu);
        root.setBorder(BorderFactory.createTitledBorder("GPIO Settings"));
        root.add(label);
        root.add(button);
        root.add(input);
        root.add(table);
        root.add(menuBar);
        translator.translateTree(root);

        require("配置".equals(label.getText()), "普通标签未汉化");
        require("应用".equals(button.getText()), "按钮未汉化");
        require("GPIO 设置".equals(((TitledBorder) root.getBorder()).getTitle()), "分组标题未汉化");
        require("配置".equals(table.getColumnModel().getColumn(0).getHeaderValue()), "表头未汉化");
        require("Reset".equals(table.getValueAt(0, 0)), "表格业务数据被翻译");
        require("Reset".equals(input.getText()), "输入内容被翻译");
        require("配置".equals(menu.getText()) && "重置".equals(menuItem.getText()), "菜单未汉化");
        require(translator.translateTree(root) == 0, "重复扫描不应再次修改普通控件");

        var actionCommand = new AtomicReference<String>();
        var actionText = new AtomicReference<String>();
        button.addActionListener(event -> {
            actionCommand.set(event.getActionCommand());
            actionText.set(button.getText());
        });
        button.doClick(0);
        require("Apply".equals(actionCommand.get()) && "Apply".equals(actionText.get()),
            "按钮回调未保留英文业务文本");
        require("应用".equals(button.getText()), "按钮点击后未恢复中文");

        button.setModel(new DefaultButtonModel());
        translator.translateTree(root);
        button.doClick(0);
        require("Apply".equals(actionCommand.get()), "替换按钮模型后英文回调失效");
        button.setText("Reset");
        translator.translateTree(root);
        button.doClick(0);
        require("Reset".equals(actionCommand.get()) && "重置".equals(button.getText()),
            "按钮文本变化后未更新翻译绑定");
        var listenerCount = ((DefaultButtonModel) button.getModel()).getChangeListeners().length;
        translator.translateTree(root);
        require(listenerCount == ((DefaultButtonModel) button.getModel()).getChangeListeners().length,
            "重复扫描注册了多余的按钮监听器");
    }

    private static JLabel tabLabel(JTabbedPane tabs, int index) {
        var header = tabs.getTabComponentAt(index);
        require(header instanceof JLabel, "未安装独立的中文标签页标题组件");
        return (JLabel) header;
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
