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
 *   <li><b>관리 플레이</b> — 선명도가 복구 순서를 정하고, 그 안에서 난이도를 고른다</li>
 * </ul>
 */
@Component
public class PlayPlanner {

    private final ClarityPolicy clarityPolicy;
    private final PurificationProperties properties;

    public PlayPlanner(ClarityPolicy clarityPolicy, PurificationProperties properties) {
        this.clarityPolicy = clarityPolicy;
        this.properties = properties;
    }

    public PlayPlan planFor(UserRelic userRelic, LocalDateTime now) {
        if (!userRelic.isCompleted()) {
            Phase phase = userRelic.getCurrentPhase();
            return new PlayPlan(EntryRoute.FIRST, phase, Phase.from(phase),
                    false, properties.firstRouteDifficulty());
        }

        Clarity clarity = clarityPolicy.clarityOf(userRelic, now);
        List<Phase> remaining = clarityPolicy.remainingPhases(clarity, userRelic.getRestorePhase());
        // 선명한 도깨비는 정령 수호만 반복
        Phase phase = remaining.isEmpty() ? Phase.GUARD : remaining.getFirst();
        List<Phase> phases = remaining.isEmpty() ? List.of(Phase.GUARD) : remaining;
        return new PlayPlan(EntryRoute.CARE, phase, phases, true, null);
    }
}
