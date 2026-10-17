package com.example.jamkkaebi.purification.dto.response;

import java.util.List;

/**
 * 이번 판의 보드 — 보드 생성기 명세 §5.1 의 입력이다.
 *
 * @param threatCount          숨겨진 위협 타일 수
 * @param reinforcedOnShape    목표 형상 안에 넣을 강화 타일 수
 * @param reinforcedOnField    형상 밖 일반 칸에 넣을 강화 타일 수
 * @param helperCount          숨겨진 도움 타일 수
 * @param bonusJeongseongCount 숨겨진 정성 보너스 타일 수
 * @param bonusCrystalCount    숨겨진 시대의 결정 보너스 타일 수
 * @param shapeCount           목표 형상 수. {@code shapeSizes} 의 길이와 같다
 * @param shapeCellTotal       목표 형상들이 차지하는 칸 수의 합. {@code shapeSizes} 의 합과 같다
 * @param shapeSizes           이번 판의 형상별 칸 수
 */
public record BoardView(
        long seed,
        int width,
        int height,
        int threatCount,
        int reinforcedOnShape,
        int reinforcedOnField,
        int helperCount,
        int bonusJeongseongCount,
        int bonusCrystalCount,
        int shapeCount,
        int shapeCellTotal,
        List<Integer> shapeSizes,
        int tapBudget,
        int scoutLimit
) {
}
