package com.example.jamkkaebi.purification.service;

import com.example.jamkkaebi.purification.config.PurificationProperties;
import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.EntryRoute;
import com.example.jamkkaebi.relic.domain.Phase;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.service.ClarityPolicy;
import org.springframework.stereotype.Component;

import java.time.LocalDateTime;
import java.util.List;

/**
 * 서버가 유물 상태로 다음 판을 정한다.
 *
 * <ul>
 *   <li><b>최초 정화</b> — 작업대에서 1→2→3페이즈 순서 고정, 난이도는 보통 고정</li>
 *   <li><b>관리 플레이</b> — 선명도가 복구 순서와 고를 수 있는 난이도를 정한다</li>
 * </ul>
 */
@Component
public class PlayPlanner {

    private final ClarityPolicy clarityPolicy;
    private final RestorePolicy restorePolicy;
    private final PurificationProperties properties;

    public PlayPlanner(ClarityPolicy clarityPolicy, RestorePolicy restorePolicy, PurificationProperties properties) {
        this.clarityPolicy = clarityPolicy;
        this.restorePolicy = restorePolicy;
        this.properties = properties;
    }

    public PlayPlan planFor(UserRelic userRelic, LocalDateTime now) {
        if (!userRelic.isCompleted()) {
            Phase phase = userRelic.getCurrentPhase();
            return new PlayPlan(EntryRoute.FIRST, phase, Phase.from(phase),
                    List.of(properties.firstRouteDifficulty()), null);
        }

        Clarity clarity = clarityPolicy.clarityOf(userRelic, now);
        // 복구를 시작한 선명도와 지금 선명도가 같을 때만 저장된 진행을 인정한다.
        // restorePhaseFor 는 GUARD 를 돌려주지 않으므로 남은 순서에는 항상 오염 관리가 있다.
        List<Phase> remaining = clarityPolicy.remainingPhases(clarity, userRelic.restorePhaseFor(clarity));
        return new PlayPlan(EntryRoute.CARE, remaining.getFirst(), remaining,
                restorePolicy.allowedFor(clarity), clarity);
    }
}
