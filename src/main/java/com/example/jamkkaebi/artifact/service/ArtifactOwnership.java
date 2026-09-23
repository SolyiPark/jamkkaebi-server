package com.example.jamkkaebi.artifact.service;

import com.example.jamkkaebi.artifact.domain.ArtifactMaster;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.example.jamkkaebi.artifact.repository.UserArtifactRepository;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import org.springframework.stereotype.Component;

/**
 * 내 유물을 꺼낸다.
 *
 * <p>유물은 하나만 존재하는 내부 행 id 가 아니라 <b>마스터 번호</b>로 가리킨다.
 * <p>없는 번호와 보유하지 않은 유물을 <b>같은 오류로</b> 돌려준다. (보유하지 않는 유물)
 */
@Component
public class ArtifactOwnership {

    private final UserArtifactRepository userArtifactRepository;
    private final ArtifactCatalog catalog;

    public ArtifactOwnership(UserArtifactRepository userArtifactRepository, ArtifactCatalog catalog) {
        this.userArtifactRepository = userArtifactRepository;
        this.catalog = catalog;
    }

    // 보유한 유물과 그 마스터. 없으면 {@code W001}.
    public Owned require(Long userId, Integer artifactId) {
        UserArtifact owned = userArtifactRepository.findByUserIdAndArtifactId(userId, artifactId)
                .orElseThrow(() -> new BusinessException(ErrorCode.ARTIFACT_NOT_OWNED));
        return new Owned(owned, catalog.artifact(artifactId));
    }

    // 정령 수호까지 마친 유물만 할 수 있는 이름 짓기·강화 확정에 사용
    public Owned requireCompleted(Long userId, Integer artifactId) {
        Owned owned = require(userId, artifactId);
        if (!owned.userArtifact().isCompleted()) {
            throw new BusinessException(ErrorCode.ARTIFACT_NOT_COMPLETED);
        }
        return owned;
    }

    // (진행 상태, 콘텐츠)
    public record Owned(UserArtifact userArtifact, ArtifactMaster master) {
    }
}
