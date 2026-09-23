package com.example.jamkkaebi.purification.config;

import com.example.jamkkaebi.artifact.domain.AwakeningStage;

import java.math.BigDecimal;

/**
 * 난이도 한 단계의 보드·자원 파라미터.
 *
 * <p>난이도는 <b>수치만 바꾼다</b>
 *
 * @param boardWidth       보드 가로 칸 수
 * @param boardHeight      보드 세로 칸 수
 * @param threatCount      숨겨진 위협 타일 수
 * @param shapeCount       찾아야 할 목표 형상 수
 * @param tapBudget        탭 예산. 도구 1회 사용마다 1 차감된다
 * @param scoutLimit       영혼의 울림(정찰) 횟수. 탭 예산과 <b>별도</b>
 * @param rewardMultiplier 보상 배율
 * @param bonusTileLimit   한 판에 매설되는 보너스 타일 수 상한
 * @param unlockStage      해금에 필요한 각성 단계
 */
public record DifficultyParams(
        int boardWidth,
        int boardHeight,
        int threatCount,
        int shapeCount,
        int tapBudget,
        int scoutLimit,
        BigDecimal rewardMultiplier,
        int bonusTileLimit,
        AwakeningStage unlockStage
) {

    public boolean unlockedFor(AwakeningStage stage) {
        return unlockStage == null || (stage != null && stage.reached(unlockStage));
    }
}
