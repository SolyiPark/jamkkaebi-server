package com.example.jamkkaebi.workbench.dto.response;

import com.example.jamkkaebi.artifact.domain.ArtifactStatus;
import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import com.example.jamkkaebi.artifact.domain.Clarity;
import com.example.jamkkaebi.artifact.domain.Phase;
import com.example.jamkkaebi.artifact.dto.response.ArtifactCard;
import com.example.jamkkaebi.artifact.dto.response.GaugeView;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.time.LocalDateTime;

/**
 * 내 유물·도깨비 상세. 작업대의 유물 상세, 도깨비 상세, 전시관에서 도깨비를 터치했을 때가 모두 이 응답을 쓴다.
 *
 * <p>진행 단계에 해당하지 않는 필드는 {@code null} 로 두어 직렬화에서 빠진다.
 *
 * @param currentPhase     최초 정화 진행도. 이미 완료했으면 {@code null}
 * @param lastClearedPhase 이번 복구 순서에서 마지막으로 클리어한 페이즈. 복구 중이 아니면 {@code null}
 * @param resealAt         이대로 두면 재봉인되는 시각. 이미 재봉인됐으면 {@code null}
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record ArtifactDetailResponse(
        ArtifactCard artifact,
        ArtifactStatus status,
        Phase currentPhase,
        AwakeningStage awakeningStage,
        GaugeView growthGauge,
        Boolean growthReady,
        AwakeningStage nextStage,
        Clarity clarity,
        LocalDateTime lastPurifiedAt,
        Phase lastClearedPhase,
        LocalDateTime resealAt,
        boolean displayed,
        boolean hidden,
        NextPlayView nextPlay
) {
}
