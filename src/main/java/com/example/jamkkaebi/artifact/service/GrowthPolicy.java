package com.example.jamkkaebi.artifact.service;

import com.example.jamkkaebi.artifact.config.GrowthProperties;
import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import org.springframework.stereotype.Component;

/**
 * 도깨비 성장 규칙.
 *
 * <p><b>게이지가 가득 차도 자동으로 승급하지 않는다</b> — 작업대에서 사용자가 강화 버튼을 눌러야
 * 확정된다. 성장 게이지는 재화로 살 수 없고 완료된 도깨비를 재플레이해야만 찬다.
 */
@Component
public class GrowthPolicy {

    private final GrowthProperties properties;

    public GrowthPolicy(GrowthProperties properties) {
        this.properties = properties;
    }

    // 다음 단계로 올라가는 데 필요한 게이지. 완전각성이면 0
    public int requiredFor(UserArtifact userArtifact) {
        AwakeningStage stage = userArtifact.getAwakeningStage();
        return stage == null ? 0 : properties.gaugeRequiredFor(stage.next());
    }

    // 강화 버튼을 누를 수 있는 상태인가.
    public boolean growthReady(UserArtifact userArtifact) {
        int required = requiredFor(userArtifact);
        return required > 0 && userArtifact.getGrowthGauge() >= required;
    }

    // 게이지를 더 채울 단계가 남았는가. 완전각성이면 게이지를 보여주지 않는다.
    public boolean hasNextStage(UserArtifact userArtifact) {
        AwakeningStage stage = userArtifact.getAwakeningStage();
        return stage != null && !stage.isMax();
    }
}
