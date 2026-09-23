package com.example.jamkkaebi.purification.dto.response;

import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import com.example.jamkkaebi.artifact.domain.Difficulty;
import com.example.jamkkaebi.purification.config.DifficultyParams;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.math.BigDecimal;

/**
 * 난이도 한 단계의 수치.
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record DifficultyView(
        Difficulty difficulty,
        int boardWidth,
        int boardHeight,
        int threatCount,
        int shapeCount,
        int tapBudget,
        int scoutLimit,
        BigDecimal rewardMultiplier,
        AwakeningStage unlockStage
) {

    public static DifficultyView of(Difficulty difficulty, DifficultyParams params) {
        return new DifficultyView(difficulty, params.boardWidth(), params.boardHeight(),
                params.threatCount(), params.shapeCount(), params.tapBudget(), params.scoutLimit(),
                params.rewardMultiplier(), params.unlockStage());
    }
}
