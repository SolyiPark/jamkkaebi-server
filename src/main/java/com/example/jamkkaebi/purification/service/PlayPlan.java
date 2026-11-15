package com.example.jamkkaebi.purification.service;

import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Difficulty;
import com.example.jamkkaebi.relic.domain.EntryRoute;
import com.example.jamkkaebi.relic.domain.Phase;

import java.util.List;

/**
 * 이 유물로 <b>다음에 할 한 판</b>. 어느 페이즈로 들어가는지, 어떤 난이도를 고를 수 있는지를 정해준다.
 *
 * @param entryRoute      최초 정화 또는 관리 플레이
 * @param phase           이번에 할 페이즈
 * @param remainingPhases 이번 것을 포함해 남은 순서
 * @param difficulties    고를 수 있는 난이도. 쉬운 순. 하나뿐이면 그 난이도로 고정
 * @param clarity         관리 플레이를 계획한 시점의 선명도. 최초 정화면 {@code null}
 */
public record PlayPlan(
        EntryRoute entryRoute,
        Phase phase,
        List<Phase> remainingPhases,
        List<Difficulty> difficulties,
        Clarity clarity
) {

    public PlayPlan {
        remainingPhases = remainingPhases == null ? List.of() : List.copyOf(remainingPhases);
        difficulties = difficulties == null ? List.of() : List.copyOf(difficulties);
    }

    public boolean allows(Difficulty difficulty) {
        return difficulties.contains(difficulty);
    }

    // 고를 것이 없으면 그 난이도, 고를 수 있으면 {@code null}
    public Difficulty fixedDifficulty() {
        return difficulties.size() == 1 ? difficulties.getFirst() : null;
    }
}
