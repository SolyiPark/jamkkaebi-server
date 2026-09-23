package com.example.jamkkaebi.purification.service;

import com.example.jamkkaebi.artifact.domain.Difficulty;
import com.example.jamkkaebi.artifact.domain.EntryRoute;
import com.example.jamkkaebi.artifact.domain.Phase;

import java.util.List;

/**
 * 이 유물로 <b>다음에 할 한 판</b>. 어느 페이즈로 들어가는지, 난이도를 고를 수 있는지를 서버가
 * 정해준다.
 *
 * @param entryRoute           최초 정화 또는 관리 플레이
 * @param phase                이번에 할 페이즈
 * @param remainingPhases      이번 것을 포함해 남은 순서
 * @param difficultySelectable 사용자가 난이도를 고를 수 있는가
 * @param fixedDifficulty      고를 수 없을 때 서버가 쓰는 난이도
 */
public record PlayPlan(
        EntryRoute entryRoute,
        Phase phase,
        List<Phase> remainingPhases,
        boolean difficultySelectable,
        Difficulty fixedDifficulty
) {

    public PlayPlan {
        remainingPhases = remainingPhases == null ? List.of() : List.copyOf(remainingPhases);
    }
}
