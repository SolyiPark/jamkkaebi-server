package com.example.jamkkaebi.purification.service;

import com.example.jamkkaebi.artifact.domain.Clarity;
import com.example.jamkkaebi.artifact.domain.EntryRoute;
import com.example.jamkkaebi.artifact.domain.Phase;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.example.jamkkaebi.artifact.service.ClarityPolicy;
import com.example.jamkkaebi.purification.config.PurificationProperties;
import org.springframework.stereotype.Component;

import java.time.LocalDateTime;
import java.util.List;

/**
 * 서버가 유물 상태로 다음 판을 정한다.
 *
 * <p>진입 경로는 둘뿐이고 코어 로직은 둘 다 같다.
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

    public PlayPlan planFor(UserArtifact userArtifact, LocalDateTime now) {
        if (!userArtifact.isCompleted()) {
            Phase phase = userArtifact.getCurrentPhase();
            return new PlayPlan(EntryRoute.FIRST, phase, Phase.from(phase),
                    false, properties.firstRouteDifficulty());
        }

        Clarity clarity = clarityPolicy.clarityOf(userArtifact, now);
        List<Phase> remaining = clarityPolicy.remainingPhases(clarity, userArtifact.getRestorePhase());
        // 선명한 도깨비는 정령 수호만 반복
        Phase phase = remaining.isEmpty() ? Phase.GUARD : remaining.getFirst();
        List<Phase> phases = remaining.isEmpty() ? List.of(Phase.GUARD) : remaining;
        return new PlayPlan(EntryRoute.CARE, phase, phases, true, null);
    }
}
