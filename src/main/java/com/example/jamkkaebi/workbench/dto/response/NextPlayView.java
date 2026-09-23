package com.example.jamkkaebi.workbench.dto.response;

import com.example.jamkkaebi.artifact.domain.EntryRoute;
import com.example.jamkkaebi.artifact.domain.Phase;
import com.example.jamkkaebi.purification.dto.response.DifficultyOption;

import java.util.List;

/**
 * 다음에 할 한 판 — 어느 페이즈로 들어가는지, 난이도를 고를 수 있는지,
 * 어떤 난이도가 열렸는지를 서버가 계산해서 준다.
 *
 * @param remainingPhases 이번 것을 포함해 남은 순서. 이미 클리어한 복구 페이즈는 빠진다
 * @param difficulties    관리 플레이면 4단계 전부(해금 여부와 함께), 최초 정화면 고정값 하나
 */
public record NextPlayView(
        EntryRoute entryRoute,
        Phase phase,
        List<Phase> remainingPhases,
        boolean difficultySelectable,
        List<DifficultyOption> difficulties
) {
}
