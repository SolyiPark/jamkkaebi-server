package com.example.jamkkaebi.purification.config;

import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import com.example.jamkkaebi.artifact.domain.Difficulty;
import org.springframework.boot.context.properties.ConfigurationProperties;

import java.math.BigDecimal;
import java.time.Duration;
import java.util.EnumMap;
import java.util.List;
import java.util.Map;

/**
 * 유물 정화의 세션·검증·보상 수치(임시)
 *
 *
 * @param sessionTtl         세션 토큰 수명. 지나면 결과를 보고할 수 없다
 * @param minPlayDuration    한 판에 최소한 걸려야 하는 시간. 이보다 빠른 보고는 거절한다
 * @param firstRouteDifficulty 최초 정화에 고정으로 쓰는 난이도 (부록 C-10 확정: 보통)
 * @param clearJeongseong    클리어 기본 정성. 여기에 난이도 배율이 곱해진다
 * @param bonusTileJeongseong 정성 보너스 타일 1개당 정성
 * @param bonusTileCrystal   시대의 결정 보너스 타일 1개당 결정
 * @param perfectBonusRate   완벽 정화 시 정성에 얹는 비율 (0.5 = 50% 추가)
 * @param growthGaugePerClear 관리 플레이 1회 클리어가 주는 성장 게이지
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
        Map<Difficulty, DifficultyParams> difficulties
) {

    // 임시값
    private static final Map<Difficulty, DifficultyParams> DEFAULT_DIFFICULTIES = Map.of(
            Difficulty.VERY_EASY, new DifficultyParams(
                    5, 6, 3, 2, 26, 3, new BigDecimal("0.8"), 2, null),
            Difficulty.EASY, new DifficultyParams(
                    6, 7, 4, 3, 23, 3, new BigDecimal("1.0"), 2, null),
            Difficulty.NORMAL, new DifficultyParams(
                    7, 8, 6, 3, 19, 3, new BigDecimal("1.2"), 3, null),
            Difficulty.HARD, new DifficultyParams(
                    8, 9, 9, 4, 16, 3, new BigDecimal("1.5"), 4, AwakeningStage.AWAKENED_PLUS));

    public PurificationProperties {
        sessionTtl = sessionTtl == null ? Duration.ofMinutes(30) : sessionTtl;
        minPlayDuration = minPlayDuration == null ? Duration.ofSeconds(5) : minPlayDuration;
        firstRouteDifficulty = firstRouteDifficulty == null ? Difficulty.NORMAL : firstRouteDifficulty;
        clearJeongseong = orDefault(clearJeongseong, 30);
        bonusTileJeongseong = orDefault(bonusTileJeongseong, 5);
        bonusTileCrystal = orDefault(bonusTileCrystal, 1);
        perfectBonusRate = perfectBonusRate == null ? new BigDecimal("0.5") : perfectBonusRate;
        growthGaugePerClear = orDefault(growthGaugePerClear, 20);

        Map<Difficulty, DifficultyParams> merged = new EnumMap<>(DEFAULT_DIFFICULTIES);
        if (difficulties != null) {
            difficulties.forEach(merged::put);
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

    private static Integer orDefault(Integer value, int defaultValue) {
        return value == null ? defaultValue : value;
    }
}
