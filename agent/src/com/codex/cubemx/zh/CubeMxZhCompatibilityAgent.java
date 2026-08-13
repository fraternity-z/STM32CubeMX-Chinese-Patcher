package com.codex.cubemx.zh;

import java.awt.EventQueue;
import java.lang.instrument.Instrumentation;

public final class CubeMxZhCompatibilityAgent {
    private CubeMxZhCompatibilityAgent() {
    }

    public static void premain(String agentArgs, Instrumentation instrumentation) {
        CubeMxZhAgent.premain(agentArgs, instrumentation);

        try {
            var dictionary = TranslationDictionary.load(CubeMxZhAgent.resolveDictionaryPath(agentArgs));
            EventQueue.invokeLater(() -> PluginTabCompatibility.install(dictionary));
        } catch (Exception exception) {
            System.err.println(
                "[STM32CubeMX zh-agent] Plugin tab compatibility disabled: "
                    + exception.getClass().getSimpleName());
        }
    }
}
