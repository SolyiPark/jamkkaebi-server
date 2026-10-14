package com.example.jamkkaebi.purification.dto.response;

import com.example.jamkkaebi.purification.config.DifficultyParams;
import com.example.jamkkaebi.relic.domain.AwakeningStage;
import com.example.jamkkaebi.relic.domain.Difficulty;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.math.BigDecimal;

/**
 * 난이도 한 단계의 수치.
 *
 * @param shapeCount     목표 형상 수
 * @param shapeCellTotal 목표 형상들이 차지하는 칸 수의 합. 형상 수와 함께 "3개/10칸" 으로 보여 준다
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record DifficultyView(
        Difficulty difficulty,
        int boardWidth,
        int boardHeight,
        int threatCount,
        int shapeCount,
        int shapeCellTotal,
        int tapBudget,
        int scoutLimit,
        BigDecimal rewardMultiplier,
        AwakeningStage unlockStage
) {

    public static DifficultyView of(Difficulty difficulty, DifficultyParams params) {
        return new DifficultyView(difficulty, params.boardWidth(), params.boardHeight(),
                params.threatCount(), params.shapeCount(), params.shapeCellTotal(),
                params.tapBudget(), params.scoutLimit(), params.rewardMultiplier(), params.unlockStage());
    }
}
