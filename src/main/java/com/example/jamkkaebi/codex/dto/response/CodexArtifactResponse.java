package com.example.jamkkaebi.codex.dto.response;

import com.example.jamkkaebi.artifact.domain.ArtifactMaster;
import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import com.example.jamkkaebi.artifact.domain.Clarity;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.example.jamkkaebi.artifact.dto.response.ArtifactCard;
import com.example.jamkkaebi.codex.domain.CodexCardState;
import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 도감 카드 상세.
 *
 * <p><b>완료({@code FULL_COLOR})한 카드에서만 실화 기록이 열린다.</b> 미완료 카드는 오류가 아니라 가려진
 * 카드를 돌려준다.
 *
 * @param artifact       유물 카드. {@code LOCKED} 면 생략
 * @param awakeningStage 각성 단계 배지. {@code FULL_COLOR} 만
 * @param clarity        선명도 배지. {@code FULL_COLOR} 만
 * @param record         실화 기록. {@code FULL_COLOR} 가 아니면 생략
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record CodexArtifactResponse(
        Integer artifactId,
        CodexCardState cardState,
        ArtifactCard artifact,
        AwakeningStage awakeningStage,
        Clarity clarity,
        CodexRecordView record
) {

    /**
     * @param owned   이 사용자의 진행 상태. 보유하지 않았으면 {@code null}
     * @param clarity 현재 선명도. 완료하지 않았으면 {@code null}
     */
    public static CodexArtifactResponse of(ArtifactMaster master, UserArtifact owned, Clarity clarity) {
        CodexCardView card = CodexCardView.of(master, owned, clarity);
        CodexRecordView record = card.cardState() == CodexCardState.FULL_COLOR
                ? new CodexRecordView(master.getRealStory(), master.getHeritageUrl(), master.getReportUrl())
                : null;
        return new CodexArtifactResponse(card.artifactId(), card.cardState(), card.artifact(),
                card.awakeningStage(), card.clarity(), record);
    }
}
