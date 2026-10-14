package com.example.jamkkaebi.relic.service;

import com.example.jamkkaebi.relic.config.GrowthProperties;
import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Phase;
import com.example.jamkkaebi.relic.domain.UserRelic;
import org.springframework.stereotype.Component;

import java.time.Duration;
import java.time.LocalDateTime;
import java.util.List;

/**
 * 선명도 계산. <b>저장된 상태가 아니라 마지막 정화 시각에서 매번 파생한다</b>.
 *
 * <p>상태를 컬럼으로 들고 있으면 "7일이 지났는데 아직 선명"이 생길 수 있다.
 * 시간은 전부 {@code GameClock} 이 주는 서버 시각으로 잰다.
 */
@Component
public class ClarityPolicy {

    private final GrowthProperties properties;

    public ClarityPolicy(GrowthProperties properties) {
        this.properties = properties;
    }

    // 현재 선명도. 아직 정령 수호를 마치지 않은 유물은 {@code null}
    public Clarity clarityOf(UserRelic userRelic, LocalDateTime now) {
        if (!userRelic.isCompleted()) {
            return null;
        }
        LocalDateTime lastPurifiedAt = userRelic.getLastPurifiedAt();
        if (lastPurifiedAt == null) {
            return Clarity.CLEAR;
        }
        Duration elapsed = Duration.between(lastPurifiedAt, now);
        if (elapsed.compareTo(properties.resealedAfter()) >= 0) {
            return Clarity.RESEALED;
        }
        if (elapsed.compareTo(properties.fadedAfter()) >= 0) {
            return Clarity.FADED;
        }
        return Clarity.CLEAR;
    }

    // 현재 기준 재봉인되는 시각. 이미 재봉인됐거나 미완료면 {@code null}.
    public LocalDateTime resealAt(UserRelic userRelic, LocalDateTime now) {
        LocalDateTime lastPurifiedAt = userRelic.getLastPurifiedAt();
        if (lastPurifiedAt == null || clarityOf(userRelic, now) == Clarity.RESEALED) {
            return null;
        }
        return lastPurifiedAt.plus(properties.resealedAfter());
    }

    // 선명으로 돌아가기까지 해야 할 페이즈
    public List<Phase> remainingPhases(Clarity clarity, Phase lastClearedPhase) {
        return Phase.from(clarity.restoreFrom()).stream()
                .filter(phase -> phase.isAfter(lastClearedPhase))
                .toList();
    }
}
