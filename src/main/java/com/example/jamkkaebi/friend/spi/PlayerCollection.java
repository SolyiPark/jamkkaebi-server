package com.example.jamkkaebi.friend.spi;

import com.example.jamkkaebi.artifact.domain.Era;

import java.util.EnumMap;
import java.util.Map;

/**
 * 사용자의 도감 진행도.
 *
 * <p>수집률은 <b>정령 수호까지 마친 유물만으로</b> 계산한다. 건물은 유물 3종을 채우면 자동으로 딸려오므로
 * 분모에 넣으면 이중 계산이 된다.
 *
 * @param completedByEra 시대별 완료 유물 수
 */
public record PlayerCollection(Map<Era, Integer> completedByEra) {

    public static final int TOTAL_ARTIFACTS = 9;

    public static final PlayerCollection NONE = new PlayerCollection(Map.of());

    public PlayerCollection {
        completedByEra = completedByEra == null || completedByEra.isEmpty()
                ? Map.of()
                : Map.copyOf(new EnumMap<>(completedByEra));
    }

    public int completedCount(Era era) {
        return completedByEra.getOrDefault(era, 0);
    }

    public int completedArtifacts() {
        return completedByEra.values().stream().mapToInt(Integer::intValue).sum();
    }

    // 수집률(%), 내림
    public int collectionRate() {
        return completedArtifacts() * 100 / TOTAL_ARTIFACTS;
    }
}
