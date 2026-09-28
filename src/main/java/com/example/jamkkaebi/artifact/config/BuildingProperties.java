package com.example.jamkkaebi.artifact.config;

import org.springframework.boot.context.properties.ConfigurationProperties;

import java.util.HashMap;
import java.util.Map;

/**
 * 건물 강화 수치(임시값).
 *
 * <p>건물 강화에는 <b>그 시대의 결정만</b> 쓴다 — 도깨비 강화와 자원을 공유하지 않는다.
 *
 * @param levelUpCost 각 레벨로 <b>올라가는 데</b> 필요한 시대의 결정. 키는 도달 레벨(2, 3).
 *                    <b>음수면 비용 미확정</b>이라 강화할 수 없다
 */
@ConfigurationProperties(prefix = "app.building")
public record BuildingProperties(
        Map<Integer, Integer> levelUpCost
) {

    private static final Map<Integer, Integer> DEFAULT_COST = Map.of(2, 30, 3, 60);

    public BuildingProperties {
        Map<Integer, Integer> merged = new HashMap<>(DEFAULT_COST);
        if (levelUpCost != null) {
            merged.putAll(levelUpCost);
        }
        levelUpCost = Map.copyOf(merged);
    }

    /**
     * {@code level} 로 올라가는 데 필요한 시대의 결정. 비용이 정해지지 않았으면 {@code null}.
     *
     * <p>미확정을 {@code null} 로만 두지 않는 이유는 {@link com.example.jamkkaebi.box.config.BoxProperties}
     * 와 같다.
     */
    public Integer costFor(int level) {
        Integer cost = levelUpCost.get(level);
        return cost == null || cost < 0 ? null : cost;
    }
}
