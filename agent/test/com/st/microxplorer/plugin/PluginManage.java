package com.st.microxplorer.plugin;

import java.util.List;

public final class PluginManage {
    private PluginManage() {
    }

    public static List<PluginView> getPlugins() {
        return List.of(new PluginView("Project Manager"));
    }
}
