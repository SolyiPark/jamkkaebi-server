package com.example.jamkkaebi.workbench.dto.response;

import com.example.jamkkaebi.relic.domain.Difficulty;
import com.example.jamkkaebi.relic.domain.EntryRoute;
import com.example.jamkkaebi.relic.domain.Phase;

import java.util.List;

/**
 * 다음에 할 한 판 — 어느 페이즈로 들어가는지, 어떤 난이도를 고를 수 있는지를 서버가 계산해서 준다.
 *
 * @param remainingPhases 이번 것을 포함해 남은 순서. 이미 클리어한 복구 페이즈는 빠진다
 * @param difficulties    고를 수 있는 난이도(쉬운 순). 하나뿐이면 그 난이도로 고정이다
 *                        — 최초 정화 {@code [NORMAL]}, 선명 {@code [EASY, NORMAL, HARD]}, 흐려짐·재봉인 {@code [NORMAL, HARD]}
 */
public record NextPlayView(
        EntryRoute entryRoute,
        Phase phase,
        List<Phase> remainingPhases,
        List<Difficulty> difficulties
) {
}
