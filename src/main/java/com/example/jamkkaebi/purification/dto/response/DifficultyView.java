package com.example.jamkkaebi.purification.dto.response;

import com.example.jamkkaebi.purification.config.DifficultyParams;
import com.example.jamkkaebi.relic.domain.AwakeningStage;
import com.example.jamkkaebi.relic.domain.Difficulty;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.math.BigDecimal;

/**
 * 난이도 한 단계의 수치.
 *
 * @param threatCount          숨겨진 위협 타일 수
 * @param reinforcedOnShape    목표 형상 안에 넣을 강화 타일 수
 * @param reinforcedOnField    형상 밖 일반 칸에 넣을 강화 타일 수
 * @param helperCount          숨겨진 도움 타일 수
 * @param bonusJeongseongCount 숨겨진 정성 보너스 타일 수
 * @param bonusCrystalCount    숨겨진 시대의 결정 보너스 타일 수
 * @param shapeCount           목표 형상 수
 * @param shapeCellTotal       목표 형상들이 차지하는 칸 수의 합
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record DifficultyView(
        Difficulty difficulty,
        int boardWidth,
        int boardHeight,
        int threatCount,
        int reinforcedOnShape,
        int reinforcedOnField,
        int helperCount,
        int bonusJeongseongCount,
        int bonusCrystalCount,
        int shapeCount,
        int shapeCellTotal,
        int tapBudget,
        int scoutLimit,
        BigDecimal rewardMultiplier,
        AwakeningStage unlockStage
) {

    public static DifficultyView of(Difficulty difficulty, DifficultyParams params) {
        return new DifficultyView(difficulty, params.boardWidth(), params.boardHeight(),
                params.threatCount(), params.reinforcedOnShape(), params.reinforcedOnField(),
                params.helperCount(), params.bonusJeongseongCount(), params.bonusCrystalCount(),
                params.shapeCount(), params.shapeCellTotal(),
                params.tapBudget(), params.scoutLimit(), params.rewardMultiplier(), params.unlockStage());
    }
}
