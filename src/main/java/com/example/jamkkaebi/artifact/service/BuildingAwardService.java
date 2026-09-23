package com.example.jamkkaebi.artifact.service;

import com.example.jamkkaebi.artifact.domain.ArtifactStatus;
import com.example.jamkkaebi.artifact.domain.BuildingMaster;
import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.example.jamkkaebi.artifact.domain.UserBuilding;
import com.example.jamkkaebi.artifact.repository.UserArtifactRepository;
import com.example.jamkkaebi.artifact.repository.UserBuildingRepository;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDateTime;
import java.util.List;
import java.util.Optional;

/**
 * 시대 완성 보상 — 유물 3종을 모두 마치면 그 시대의 건물을 자동으로 지급한다.
 *
 * <p>건물에는 진행도가 없고 지급 조건도 저장하지 않는다.
 */
@Service
public class BuildingAwardService {

    private final UserArtifactRepository userArtifactRepository;
    private final UserBuildingRepository userBuildingRepository;
    private final ArtifactCatalog catalog;

    public BuildingAwardService(UserArtifactRepository userArtifactRepository,
                                UserBuildingRepository userBuildingRepository,
                                ArtifactCatalog catalog) {
        this.userArtifactRepository = userArtifactRepository;
        this.userBuildingRepository = userBuildingRepository;
        this.catalog = catalog;
    }

    /**
     * 이번 완료로 시대 유물 3종이 채워졌으면 건물을 지급한다.
     *
     * @return 이번에 지급된 건물. 아직 3종이 다 모이지 않았거나 이미 가지고 있으면 비어 있다.
     */
    @Transactional
    public Optional<Awarded> awardIfEraCompleted(Long userId, Era era, LocalDateTime now) {
        Optional<BuildingMaster> building = catalog.buildingOf(era);
        if (building.isEmpty() || userBuildingRepository.existsByUserIdAndBuildingId(userId, building.get().getId())) {
            return Optional.empty();
        }
        if (!eraCompleted(userId, era)) {
            return Optional.empty();
        }
        UserBuilding awarded = userBuildingRepository.save(
                UserBuilding.awarded(userId, building.get().getId(), now));
        return Optional.of(new Awarded(building.get(), awarded));
    }

    private boolean eraCompleted(Long userId, Era era) {
        List<Integer> eraArtifactIds = catalog.artifactIdsOf(era);
        long completed = userArtifactRepository.findAllByUserIdAndStatus(userId, ArtifactStatus.COMPLETED).stream()
                .map(UserArtifact::getArtifactId)
                .filter(eraArtifactIds::contains)
                .count();
        return completed >= eraArtifactIds.size();
    }

    public record Awarded(BuildingMaster master, UserBuilding owned) {
    }
}
