package com.example.jamkkaebi.artifact.config;

import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import org.springframework.boot.context.properties.ConfigurationProperties;

import java.time.Duration;
import java.util.EnumMap;
import java.util.Map;

/**
 * 선명도와 도깨비 성장 수치.
 *
 * <p>기간은 API 명세 부록 C-5 확정값(7일 흐려짐 / 14일 재봉인)이고, 게이지 요구량은 재화 기획
 * 확정 전의 임시값이다. <b>숫자를 코드에 박지 않는 이유</b>는 확정될 때 전부 찾아 고쳐야 하기
 * 때문이다 — 로직은 지금 완성해두고 값만 나중에 넣는다 (백엔드 계획 결정 ⑤).
 *
 * @param fadedAfter    이 기간을 넘겨 방치하면 흐려진다
 * @param resealedAfter 이 기간을 넘겨 방치하면 재봉인된다
 * @param resealWarningBefore 재봉인 며칠 전에 예고 알림을 보낼지 (알림 구현은 M4)
 * @param gaugeRequired 각 단계로 <b>올라가는 데</b> 필요한 성장 게이지
 */
@ConfigurationProperties(prefix = "app.growth")
public record GrowthProperties(
        Duration fadedAfter,
        Duration resealedAfter,
        Duration resealWarningBefore,
        Map<AwakeningStage, Integer> gaugeRequired
) {

    private static final Map<AwakeningStage, Integer> DEFAULT_GAUGE = Map.of(
            AwakeningStage.AWAKENED_PLUS, 100,
            AwakeningStage.FULLY_AWAKENED, 150);

    public GrowthProperties {
        fadedAfter = fadedAfter == null ? Duration.ofDays(7) : fadedAfter;
        resealedAfter = resealedAfter == null ? Duration.ofDays(14) : resealedAfter;
        resealWarningBefore = resealWarningBefore == null ? Duration.ofDays(3) : resealWarningBefore;

        Map<AwakeningStage, Integer> merged = new EnumMap<>(DEFAULT_GAUGE);
        if (gaugeRequired != null) {
            merged.putAll(gaugeRequired);
        }
        gaugeRequired = Map.copyOf(merged);
    }

    /**
     * {@code stage} 로 올라가는 데 필요한 게이지. 완전각성 다음은 없으므로 {@code null} 이면 0.
     */
    public int gaugeRequiredFor(AwakeningStage stage) {
        return stage == null ? 0 : gaugeRequired.getOrDefault(stage, 0);
    }
}
