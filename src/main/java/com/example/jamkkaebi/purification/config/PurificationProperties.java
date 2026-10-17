package com.example.jamkkaebi.purification.config;

import com.example.jamkkaebi.relic.domain.AwakeningStage;
import com.example.jamkkaebi.relic.domain.Difficulty;
import org.springframework.boot.context.properties.ConfigurationProperties;

import java.math.BigDecimal;
import java.time.Duration;
import java.util.EnumMap;
import java.util.List;
import java.util.Map;
import java.util.stream.Stream;

/**
 * 유물 정화의 세션·검증·보상 수치(임시)
 *
 *
 * @param sessionTtl         세션 토큰 수명
 * @param minPlayDuration    한 판에 최소한 걸려야 하는 시간
 * @param firstRouteDifficulty 최초 정화에 고정으로 쓰는 난이도 (보통)
 * @param clearJeongseong    클리어 기본 정성. 난이도 배율을 곱한다
 * @param bonusTileJeongseong 정성 보너스 타일 1개당 정성
 * @param bonusTileCrystal   시대의 결정 보너스 타일 1개당 결정
 * @param perfectBonusRate   완벽 정화 시 추가 정성 비율
 * @param growthGaugePerClear 관리 플레이 1회 클리어가 주는 성장 게이지
 * @param shapeSizeMin       목표 형상 하나의 최소 칸 수
 * @param shapeSizeMax       목표 형상 하나의 최대 칸 수
 * @param difficulties       난이도별 파라미터
 */
@ConfigurationProperties(prefix = "app.purification")
public record PurificationProperties(
        Duration sessionTtl,
        Duration minPlayDuration,
        Difficulty firstRouteDifficulty,
        Integer clearJeongseong,
        Integer bonusTileJeongseong,
        Integer bonusTileCrystal,
        BigDecimal perfectBonusRate,
        Integer growthGaugePerClear,
        Integer shapeSizeMin,
        Integer shapeSizeMax,
        Map<Difficulty, DifficultyParams> difficulties
) {

    //  - 탭·위협: 위협 3개에 실패할 때 보통 플레이어 성공률이 약 100 · 84 · 71 · 49% 로 고르게 내려가도록 맞춘 값
    //  - 강화: 2 · 2 · 3 · 4칸 중 절반은 형상 안에.
    //  - 도움 1칸 (어려움만 2칸)
    //  - 보너스: 보드의 약 5%. (시대의 결정 1개+나머지는 정성)
    private static final Map<Difficulty, DifficultyParams> DEFAULT_DIFFICULTIES = Map.of(
            Difficulty.VERY_EASY, new DifficultyParams(
                    5, 6, 2, 1, 1, 1, 1, 1, 3, 6, 12, 3, new BigDecimal("0.8"), null),
            Difficulty.EASY, new DifficultyParams(
                    6, 7, 4, 1, 1, 1, 1, 1, 3, 8, 16, 3, new BigDecimal("1.0"), null),
            Difficulty.NORMAL, new DifficultyParams(
                    7, 8, 6, 2, 1, 1, 2, 1, 3, 10, 19, 3, new BigDecimal("1.2"), null),
            Difficulty.HARD, new DifficultyParams(
                    8, 9, 8, 2, 2, 2, 3, 1, 4, 14, 20, 3, new BigDecimal("1.5"), AwakeningStage.AWAKENED_PLUS));

    public PurificationProperties {
        sessionTtl = sessionTtl == null ? Duration.ofMinutes(30) : sessionTtl;
        minPlayDuration = minPlayDuration == null ? Duration.ofSeconds(5) : minPlayDuration;
        firstRouteDifficulty = firstRouteDifficulty == null ? Difficulty.NORMAL : firstRouteDifficulty;
        clearJeongseong = orDefault(clearJeongseong, 30);
        bonusTileJeongseong = orDefault(bonusTileJeongseong, 5);
        bonusTileCrystal = orDefault(bonusTileCrystal, 1);
        perfectBonusRate = perfectBonusRate == null ? new BigDecimal("0.5") : perfectBonusRate;
        growthGaugePerClear = orDefault(growthGaugePerClear, 20);
        shapeSizeMin = orDefault(shapeSizeMin, 2);
        shapeSizeMax = orDefault(shapeSizeMax, 5);
        if (shapeSizeMin < 1 || shapeSizeMin > shapeSizeMax) {
            throw new IllegalArgumentException("목표 형상 크기 범위가 올바르지 않습니다: %d~%d칸"
                    .formatted(shapeSizeMin, shapeSizeMax));
        }

        // 설정에 적은 값만 덮어쓰고, 적지 않은 값은 그 난이도의 기본값을 쓴다.
        Map<Difficulty, DifficultyParams> merged = new EnumMap<>(DEFAULT_DIFFICULTIES);
        if (difficulties != null) {
            difficulties.forEach((difficulty, override) ->
                    merged.put(difficulty, override.withDefaults(DEFAULT_DIFFICULTIES.get(difficulty))));
        }
        for (Map.Entry<Difficulty, DifficultyParams> entry : merged.entrySet()) {
            requireBuildable(entry.getKey(), entry.getValue(), shapeSizeMin, shapeSizeMax);
        }
        difficulties = Map.copyOf(merged);
    }

    // 난이도 파라미터
    public DifficultyParams paramsOf(Difficulty difficulty) {
        return difficulties.get(difficulty);
    }

    public List<Difficulty> ordered() {
        return List.of(Difficulty.values());
    }

    //칸 수 합이 형상 개수로 나눌 수 없는 값이거나 보드에 다 들어가지 않으면 뜨지 않게 막는다
    private static void requireBuildable(Difficulty difficulty, DifficultyParams params,
                                         int shapeSizeMin, int shapeSizeMax) {
        if (!params.complete()) {
            throw invalid(difficulty, "비어 있는 수치가 있습니다: " + params);
        }
        if (params.shapeCount() < 1) {
            throw invalid(difficulty, "목표 형상은 1개 이상이어야 합니다.");
        }
        int fewest = params.shapeCount() * shapeSizeMin;
        int most = params.shapeCount() * shapeSizeMax;
        if (params.shapeCellTotal() < fewest || params.shapeCellTotal() > most) {
            throw invalid(difficulty, "형상 %d개의 칸 수 합은 %d~%d 이어야 합니다. 현재 %d"
                    .formatted(params.shapeCount(), fewest, most, params.shapeCellTotal()));
        }
        if (Stream.of(params.threatCount(), params.reinforcedOnShape(), params.reinforcedOnField(),
                        params.helperCount(), params.bonusJeongseongCount(), params.bonusCrystalCount())
                .anyMatch(count -> count < 0)) {
            throw invalid(difficulty, "타일 수는 0 이상이어야 합니다.");
        }
        // 생성 순서: 형상 → 위협 → 도움 → 강화 → 보너스
        int bonus = params.bonusJeongseongCount() + params.bonusCrystalCount();
        if (bonus > params.plainCells()) {
            int occupied = params.boardCells() - params.plainCells() + bonus;
            throw invalid(difficulty, "형상·위협·도움·보너스 타일 %d칸이 보드 %d칸에 들어가지 않습니다."
                    .formatted(occupied, params.boardCells()));
        }
        if (params.reinforcedOnShape() > params.shapeCellTotal()) {
            throw invalid(difficulty, "형상 안 강화 타일 %d칸이 형상 칸 수 %d를 넘습니다."
                    .formatted(params.reinforcedOnShape(), params.shapeCellTotal()));
        }
        if (params.reinforcedOnField() > params.plainCells()) {
            throw invalid(difficulty, "형상 밖 강화 타일 %d칸을 놓을 일반 칸이 %d칸뿐입니다."
                    .formatted(params.reinforcedOnField(), params.plainCells()));
        }
    }

    private static IllegalArgumentException invalid(Difficulty difficulty, String reason) {
        return new IllegalArgumentException("난이도 %s 설정이 올바르지 않습니다 — %s".formatted(difficulty, reason));
    }

    private static Integer orDefault(Integer value, int defaultValue) {
        return value == null ? defaultValue : value;
    }
}
