package com.example.jamkkaebi.relic.config;

import org.springframework.boot.context.properties.ConfigurationProperties;

import java.time.Duration;

/**
 * 선명도 기간. 7일 흐려짐 / 14일 재봉인.
 *
 * @param fadedAfter          이 기간을 넘겨 방치하면 흐려진다
 * @param resealedAfter       이 기간을 넘겨 방치하면 재봉인된다
 * @param resealWarningBefore 재봉인 며칠 전에 예고 알림을 보낼지 (알림 구현은 M4)
 */
@ConfigurationProperties(prefix = "app.clarity")
public record ClarityProperties(
        Duration fadedAfter,
        Duration resealedAfter,
        Duration resealWarningBefore
) {

    public ClarityProperties {
        fadedAfter = fadedAfter == null ? Duration.ofDays(7) : fadedAfter;
        resealedAfter = resealedAfter == null ? Duration.ofDays(14) : resealedAfter;
        resealWarningBefore = resealWarningBefore == null ? Duration.ofDays(3) : resealWarningBefore;
    }
}
