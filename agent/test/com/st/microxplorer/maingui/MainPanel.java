package com.st.microxplorer.maingui;

import javax.swing.JLabel;
import javax.swing.JPanel;
import javax.swing.JTabbedPane;

public final class MainPanel extends JPanel {
    private final JTabbedPane viewsTabPane = new JTabbedPane();

    public MainPanel() {
        var tab = new JPanel();
        tab.add(new JLabel("工程管理器"));
        viewsTabPane.addTab("工程管理器", new JPanel());
        viewsTabPane.setTabComponentAt(0, tab);
        add(viewsTabPane);
    }

    public JPanel getTabComponent() {
        return (JPanel) viewsTabPane.getTabComponentAt(0);
    }

    public String getSelectedPluginView() {
        for (var component : getTabComponent().getComponents()) {
            if (component instanceof JLabel label && label.getIcon() == null) {
                return label.getText();
            }
        }
        return null;
    }
}
