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
 *   <li><b>관리 플레이</b> — 지금 선명도가 페이즈와 고를 수 있는 난이도를 정한다
 *       (재봉인 → 봉인 해제, 흐려짐 → 기억 회복, 선명 → 오염 관리)</li>
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
        return new PlayPlan(EntryRoute.CARE, clarity.restoreFrom(), clarityPolicy.remainingPhases(clarity),
                restorePolicy.allowedFor(clarity), clarity);
    }
}
