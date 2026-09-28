package com.example.jamkkaebi.workbench.service;

import com.example.jamkkaebi.artifact.config.BuildingProperties;
import com.example.jamkkaebi.artifact.domain.BuildingMaster;
import com.example.jamkkaebi.artifact.domain.UserBuilding;
import com.example.jamkkaebi.artifact.repository.UserBuildingRepository;
import com.example.jamkkaebi.artifact.service.ArtifactCatalog;
import com.example.jamkkaebi.artifact.service.EraCrystalWallet;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import com.example.jamkkaebi.user.service.UserProfileService;
import com.example.jamkkaebi.workbench.dto.response.BuildingDetailResponse;
import com.example.jamkkaebi.workbench.dto.response.BuildingLevelUpResponse;
import com.example.jamkkaebi.workbench.dto.response.NextLevelView;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * 작업대 — 건물 상세 조회와 강화.
 *
 * <p>건물은 뽑기·플레이 대상이 아니라 시대 완성 보상으로 받는다. 받은 뒤에는 <b>그 시대의 결정만</b>
 * 써서 강화한다 — 도깨비 강화(게이지)와 자원을 공유하지 않는다.
 */
@Service
public class BuildingService {

    private final UserProfileService userProfileService;
    private final UserRepository userRepository;
    private final UserBuildingRepository userBuildingRepository;
    private final ArtifactCatalog catalog;
    private final EraCrystalWallet wallet;
    private final BuildingProperties properties;

    public BuildingService(UserProfileService userProfileService,
                           UserRepository userRepository,
                           UserBuildingRepository userBuildingRepository,
                           ArtifactCatalog catalog,
                           EraCrystalWallet wallet,
                           BuildingProperties properties) {
        this.userProfileService = userProfileService;
        this.userRepository = userRepository;
        this.userBuildingRepository = userBuildingRepository;
        this.catalog = catalog;
        this.wallet = wallet;
        this.properties = properties;
    }

    @Transactional(readOnly = true)
    public BuildingDetailResponse detail(String friendCode, Integer buildingId) {
        User user = userProfileService.getByFriendCode(friendCode);
        UserBuilding owned = userBuildingRepository.findByUserIdAndBuildingId(user.getId(), buildingId)
                .orElseThrow(() -> new BusinessException(ErrorCode.BUILDING_NOT_OWNED));
        BuildingMaster master = catalog.building(buildingId);
        int balance = wallet.balance(user.getId(), master.getEra());

        return new BuildingDetailResponse(
                master.getId(),
                master.getEra(),
                master.getName(),
                owned.getLevel(),
                BuildingMaster.MAX_LEVEL,
                master.getRealStory(),
                owned.getAcquiredAt(),
                owned.isDisplayed(),
                balance,
                nextLevelOf(owned, balance));
    }

    /**
     * 강화
     *
     * <p>사용자 행을 잠그고 시작해 같은 사용자의 강화 요청을 줄 세운다. 결정 차감은 잔액 조건이 붙은
     * UPDATE 한 문장이라, 같은 시대의 결정이 정화 정산으로 동시에 들어와도 적립분을 덮어쓰지 않는다.
     */
    @Transactional
    public BuildingLevelUpResponse levelUp(String friendCode, Integer buildingId, int targetLevel) {
        User user = userRepository.findByFriendCodeForUpdate(friendCode)
                .orElseThrow(() -> new BusinessException(ErrorCode.USER_NOT_FOUND));
        UserBuilding owned = userBuildingRepository.findByUserIdAndBuildingId(user.getId(), buildingId)
                .orElseThrow(() -> new BusinessException(ErrorCode.BUILDING_NOT_OWNED));
        BuildingMaster master = catalog.building(buildingId);
        int current = owned.getLevel();

        if (targetLevel == current) {
            // 응답을 못 받고 다시 누른 경우. (멱등)
            int balance = wallet.balance(user.getId(), master.getEra());
            return new BuildingLevelUpResponse(current, 0, balance, nextLevelOf(owned, balance));
        }
        if (targetLevel != current + 1) {
            throw new BusinessException(ErrorCode.BUILDING_LEVEL_MISMATCH);
        }

        Integer cost = properties.costFor(targetLevel);
        if (cost == null || !wallet.spend(user.getId(), master.getEra(), cost)) {
            throw new BusinessException(ErrorCode.NOT_ENOUGH_ERA_CRYSTAL);
        }
        owned.levelUp();

        int balance = wallet.balance(user.getId(), master.getEra());
        return new BuildingLevelUpResponse(owned.getLevel(), cost, balance, nextLevelOf(owned, balance));
    }

    private NextLevelView nextLevelOf(UserBuilding owned, int balance) {
        if (owned.getLevel() >= BuildingMaster.MAX_LEVEL) {
            return null;
        }
        int next = owned.getLevel() + 1;
        return NextLevelView.of(next, properties.costFor(next), balance);
    }
}
