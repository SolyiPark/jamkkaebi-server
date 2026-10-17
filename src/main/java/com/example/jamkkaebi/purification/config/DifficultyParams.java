package com.example.jamkkaebi.purification.config;

import com.example.jamkkaebi.relic.domain.AwakeningStage;

import java.math.BigDecimal;

/**
 * 난이도 한 단계의 보드·자원 파라미터.
 *
 * <p>난이도는 <b>수치만 바꾼다</b>
 *
 * <p>목표 형상은 <b>형상 개수와 칸 수 합의 쌍</b>으로 정한다.
 * 합을 형상마다 몇 칸씩 나눌지는 판마다 보드 시드로 정한다({@code ShapeSizePlanner}).
 *
 * <p>특수 타일은 종류마다 개수만 정하고, 어느 칸에 놓을지는 클라이언트가 보드 시드로 정한다(보드 생성기 명세 §6).
 * 위협·도움·보너스 타일은 한 칸씩 차지한다. 강화 타일은 칸의 종류를 바꾸지 않고 2회 타격 표시만 얹는다.
 *
 * <p>설정({@code app.purification.difficulties.<난이도>.*})에서 난이도만 바꾸면 나머지는
 * 기본값으로 채워진다.
 *
 * @param boardWidth           보드 가로 칸 수
 * @param boardHeight          보드 세로 칸 수
 * @param threatCount          숨겨진 위협 타일 수
 * @param reinforcedOnShape    목표 형상 안에 넣을 강화 타일 수
 * @param reinforcedOnField    형상 밖 일반 칸에 넣을 강화 타일 수
 * @param helperCount          숨겨진 도움 타일 수
 * @param bonusJeongseongCount 숨겨진 정성 보너스 타일 수
 * @param bonusCrystalCount    숨겨진 시대의 결정 보너스 타일 수
 * @param shapeCount           찾아야 할 목표 형상 수
 * @param shapeCellTotal       목표 형상들이 차지하는 칸 수의 합
 * @param tapBudget            탭 예산
 * @param scoutLimit           정찰 횟수
 * @param rewardMultiplier     보상 배율
 * @param unlockStage          해금에 필요한 각성 단계. 기본값이 있는 난이도(어려움)를 늘 열어 두려면
 *                             {@code AWAKENED} 로 둔다.
 */
public record DifficultyParams(
        Integer boardWidth,
        Integer boardHeight,
        Integer threatCount,
        Integer reinforcedOnShape,
        Integer reinforcedOnField,
        Integer helperCount,
        Integer bonusJeongseongCount,
        Integer bonusCrystalCount,
        Integer shapeCount,
        Integer shapeCellTotal,
        Integer tapBudget,
        Integer scoutLimit,
        BigDecimal rewardMultiplier,
        AwakeningStage unlockStage
) {

    public boolean unlockedFor(AwakeningStage stage) {
        return unlockStage == null || (stage != null && stage.reached(unlockStage));
    }

    public int boardCells() {
        return boardWidth * boardHeight;
    }

    // 형상·위협·도움 타일을 놓고 남는 일반 칸. 형상 밖 강화와 보너스가 여기에 놓인다.
    public int plainCells() {
        return boardCells() - shapeCellTotal - threatCount - helperCount;
    }

    // 비어 있는 값을 기본값으로 채운다.
    public DifficultyParams withDefaults(DifficultyParams defaults) {
        return new DifficultyParams(
                orDefault(boardWidth, defaults.boardWidth),
                orDefault(boardHeight, defaults.boardHeight),
                orDefault(threatCount, defaults.threatCount),
                orDefault(reinforcedOnShape, defaults.reinforcedOnShape),
                orDefault(reinforcedOnField, defaults.reinforcedOnField),
                orDefault(helperCount, defaults.helperCount),
                orDefault(bonusJeongseongCount, defaults.bonusJeongseongCount),
                orDefault(bonusCrystalCount, defaults.bonusCrystalCount),
                orDefault(shapeCount, defaults.shapeCount),
                orDefault(shapeCellTotal, defaults.shapeCellTotal),
                orDefault(tapBudget, defaults.tapBudget),
                orDefault(scoutLimit, defaults.scoutLimit),
                orDefault(rewardMultiplier, defaults.rewardMultiplier),
                orDefault(unlockStage, defaults.unlockStage));
    }

    // 해금 조건을 뺀 값이 모두 있는가
    boolean complete() {
        return boardWidth != null && boardHeight != null && threatCount != null
                && reinforcedOnShape != null && reinforcedOnField != null && helperCount != null
                && bonusJeongseongCount != null && bonusCrystalCount != null && shapeCount != null
                && shapeCellTotal != null && tapBudget != null && scoutLimit != null
                && rewardMultiplier != null;
    }

    private static <T> T orDefault(T value, T defaultValue) {
        return value == null ? defaultValue : value;
    }
}
