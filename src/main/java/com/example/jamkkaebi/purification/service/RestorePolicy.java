package com.example.jamkkaebi.purification.service;

import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Difficulty;
import org.springframework.stereotype.Component;

import java.util.Arrays;
import java.util.List;

/**
 * 난이도별 복구 효과와, 선명도에 따라 관리 플레이에서 고를 수 있는 난이도. (기획 확정값 2026-11-15)
 *
 * <ul>
 *   <li>쉬움 — 복구 없이 선명도 회복만. 선명할 때만 고를 수 있다</li>
 *   <li>보통 — 한 페이즈 복구</li>
 *   <li>어려움 — 한 판에 전체 복구</li>
 * </ul>
 */
@Component
public class RestorePolicy {

    public RestoreEffect effectOf(Difficulty difficulty) {
        return switch (difficulty) {
            case EASY -> RestoreEffect.REFRESH_ONLY;
            case NORMAL -> RestoreEffect.ONE_STEP;
            case HARD -> RestoreEffect.FULL;
        };
    }

    // 이 선명도에서 관리 플레이로 고를 수 있는 난이도. 쉬운 순
    public List<Difficulty> allowedFor(Clarity clarity) {
        return Arrays.stream(Difficulty.values())
                .filter(difficulty -> clarity == Clarity.CLEAR || effectOf(difficulty) != RestoreEffect.REFRESH_ONLY)
                .toList();
    }
}
