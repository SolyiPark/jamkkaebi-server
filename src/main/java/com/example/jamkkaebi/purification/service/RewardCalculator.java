package com.example.jamkkaebi.purification.service;

import com.example.jamkkaebi.artifact.domain.EntryRoute;
import com.example.jamkkaebi.purification.config.DifficultyParams;
import com.example.jamkkaebi.purification.config.PurificationProperties;
import org.springframework.stereotype.Component;

import java.math.BigDecimal;
import java.math.RoundingMode;

/**
 * 한 판의 보상을 계산한다. (임시 수치값)
 */
@Component
public class RewardCalculator {

    private final PurificationProperties properties;

    public RewardCalculator(PurificationProperties properties) {
        this.properties = properties;
    }

    /**
     * @param perfect 손상 0으로 클리어 + 보너스 대상 경로인가
     */
    public Reward calculate(DifficultyParams params, EntryRoute entryRoute, boolean success, boolean perfect,
                            int bonusJeongseongTiles, int bonusCrystalTiles) {
        if (!success) {
            return Reward.NONE;
        }

        BigDecimal multiplier = params.rewardMultiplier();
        BigDecimal perfectFactor = perfect
                ? BigDecimal.ONE.add(properties.perfectBonusRate())
                : BigDecimal.ONE;

        int jeongseong = round(BigDecimal.valueOf(properties.clearJeongseong())
                .multiply(multiplier)
                .multiply(perfectFactor))
                + bonusJeongseongTiles * properties.bonusTileJeongseong();

        int crystal = bonusCrystalTiles * properties.bonusTileCrystal();

        int gauge = entryRoute == EntryRoute.CARE
                ? round(BigDecimal.valueOf(properties.growthGaugePerClear()).multiply(multiplier))
                : 0;

        return new Reward(jeongseong, crystal, gauge);
    }

    private static int round(BigDecimal value) {
        return value.setScale(0, RoundingMode.HALF_UP).intValue();
    }

    /**
     * 정산된 보상.
     *
     * @param jeongseong 범용 재화
     * @param crystal    해당 시대의 결정
     * @param gauge      성장 게이지
     */
    public record Reward(int jeongseong, int crystal, int gauge) {

        public static final Reward NONE = new Reward(0, 0, 0);
    }
}
